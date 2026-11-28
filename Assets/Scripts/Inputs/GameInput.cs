using System;
using Ring.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ring.Inputs
{
    /// <summary>
    /// 输入系统的运行时入口：持有当前映射表，每帧轮询旧版 Input，向外提供
    /// 「按下 / 按住 / 抬起」三种状态、改键 API，以及 M0 的唯一输出 <see cref="InputIntent"/>。
    ///
    /// 之所以用旧版 InputManager 而不是新 Input System：
    /// 工程当前没有安装 com.unity.inputsystem（见 Packages/manifest.json），
    /// ProjectSettings 的 activeInputHandler = 0（旧版 Input Manager）。
    /// 为了不给团队加依赖，这里直接用 UnityEngine.Input；映射表本身是纯数据，
    /// 将来换成新系统只需要改本文件的轮询部分。
    ///
    /// D12「鼠标选点」也在这里收口，链路是：
    /// UnityEngine.Input.mousePosition（屏幕）
    ///   → Camera.ScreenPointToRay 与 z = gamePlaneZ 平面求交（世界 XY）
    ///   → <see cref="GridSpace.WorldToCell"/>（floor 取整，得到格坐标）
    ///   → 写进 <see cref="Intent"/> 的 aimPoint，供 M2 / M3 / M6 消费。
    /// 相机与格参数都在 Inspector 里手工指派，本类不创建任何场景物体或 UI。
    /// </summary>
    // 早于所有消费者执行：保证 AbilityCaster / PlayerMotor 读到的 Intent 是本帧最新快照，
    // 而不是上一帧（Unity 的 Update 顺序默认不确定）。
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/输入系统 GameInput")]
    public sealed class GameInput : MonoBehaviour
    {
        /// <summary>场景内的当前实例（跨场景保留）。</summary>
        public static GameInput Instance { get; private set; }

        [Tooltip("输入映射资产。为空时直接用代码里的文档默认键位。")]
        [SerializeField] private InputSettings settings;

        [Tooltip("跨场景保留这个组件。")]
        [SerializeField] private bool persistAcrossScenes = true;

        [Tooltip("改键后打印一条日志，方便调试。")]
        [SerializeField] private bool logRebinds;

        [Header("鼠标选点 D12（屏幕 → 世界 → 格坐标）")]
        [Tooltip("投影用的相机，请手工拖进来。留空时自动取 Camera.main（需要场景相机带 MainCamera 标签）。")]
        [SerializeField] private Camera worldCamera;

        [Tooltip("格坐标换算规则。CellSize 必须和 PlayerMotor / AbilityCaster 的 unitsPerCell 一致。")]
        [SerializeField] private GridSpace grid = GridSpace.DocumentDefault;

        [Tooltip("游戏平面所在的 Z。2D 俯视通常是 0；屏幕射线与这个平面求交得到世界 XY。")]
        [SerializeField] private float gamePlaneZ = 0f;

        [Tooltip("Hover = 每帧跟随鼠标（默认，不占用按键）；Click = 按指定鼠标键才锁定选点并触发 AimPicked。")]
        [SerializeField] private AimMode aimMode = AimMode.Hover;

        [Tooltip("Click 模式下确认选点的鼠标键：0 左 / 1 右 / 2 中。默认中键，避开平A(左)与闪避(右)。")]
        [Range(0, 2)]
        [SerializeField] private int aimClickButton = 2;

        [Tooltip("鼠标压在 uGUI 上时忽略选点（需要场景里有 EventSystem；没有时该开关不生效）。")]
        [SerializeField] private bool ignorePointerOverUI = true;

        [Tooltip("把选点结果打印到 Console，方便验收 D12。Hover 模式仅在跨格时打印，不会刷屏。")]
        [SerializeField] private bool logAimPicks;

        [Tooltip("把每个动作的「按下」打印出来，用来区分「键没收到」和「收到了但技能没生效」。排查期建议打开。")]
        [SerializeField] private bool logActionPresses;

        private readonly bool[] pressed = new bool[InputActions.Count];
        private readonly bool[] held = new bool[InputActions.Count];
        private readonly bool[] released = new bool[InputActions.Count];

        // 把枚举先转成 KeyCode 缓存起来，避免每帧对每个动作做字符串解析。
        private readonly KeyCode[,] keyCodes = new KeyCode[InputActions.Count, InputBindingTable.SlotsPerAction];

        private bool keyCodesDirty = true;

        // 界面压住游戏输入的层数（改键面板等）。大于 0 时 Poll 直接跳过。
        private int gameplaySuspensions;

        // 每帧重算的输入意图快照（D3：玩家与 AI 同构的唯一入口）。
        private InputIntent intent;

        // 最近一次有效选点（Click 模式下会一直保留到下一次确认）。
        private bool aimValid;
        private Vector2 aimWorldPoint;
        private GridCoord aimCell;
        private bool aimInsideGrid;
        private GridCoord lastLoggedCell;
        private bool hasLoggedCell;

        /// <summary>某个动作被按下时触发（每帧最多一次）。</summary>
        public event Action<InputActionId> ActionPressed;

        /// <summary>某个动作被抬起时触发。</summary>
        public event Action<InputActionId> ActionReleased;

        /// <summary>每帧算好输入意图后触发（M2 / M6 可订阅，与输入来自玩家还是 AI 无关）。</summary>
        public event Action<InputIntent> IntentUpdated;

        /// <summary>Click 模式下确认了一次选点时触发，参数就是落点格坐标。</summary>
        public event Action<GridCoord> AimPicked;

        /// <summary>当前映射表。</summary>
        public InputBindingTable Bindings { get; private set; }

        /// <summary>当前输入配置资产（可能为空）。</summary>
        public InputSettings Settings => settings;

        /// <summary>本帧的输入意图快照（M0 的唯一输出，D3 同构入口）。</summary>
        public InputIntent Intent
        {
            get { return intent; }
        }

        /// <summary>当前格坐标换算规则（只读副本；改配置用 SetGrid / SetGridOrigin）。</summary>
        public GridSpace Grid
        {
            get { return grid; }
        }

        /// <summary>当前选点取样方式。</summary>
        public AimMode CurrentAimMode
        {
            get { return aimMode; }
        }

        /// <summary>投影用的相机；Inspector 未指派时回退 Camera.main。</summary>
        public Camera EffectiveCamera
        {
            get { return worldCamera != null ? worldCamera : Camera.main; }
        }

        /// <summary>最近一次有效选点的世界坐标。</summary>
        public Vector2 AimWorldPoint
        {
            get { return aimWorldPoint; }
        }

        /// <summary>最近一次有效选点的格坐标。</summary>
        public GridCoord AimCell
        {
            get { return aimCell; }
        }

        /// <summary>是否存在有效选点（相机缺失 / 射线未命中 / 被 UI 挡住时为 false）。</summary>
        public bool HasValidAim
        {
            get { return aimValid; }
        }

        /// <summary>最近一次有效选点是否落在配置的地图边界内。</summary>
        public bool AimInsideGrid
        {
            get { return aimInsideGrid; }
        }

        /// <summary>是否允许运行时改键。</summary>
        public bool CanRebind => settings == null || settings.AllowRuntimeRebinding;

        /// <summary>改键结果是否写 PlayerPrefs。</summary>
        public bool PersistsBindings => settings == null || settings.PersistToPlayerPrefs;

        /// <summary>鼠标屏幕坐标，供瞄准使用。</summary>
        public Vector2 PointerScreenPosition => UnityEngine.Input.mousePosition;

        /// <summary>移动方向（已归一化，斜向不会更快）。</summary>
        public Vector2 MoveAxis
        {
            get
            {
                float x = 0f;
                float y = 0f;
                if (held[(int)InputActionId.MoveLeft])
                {
                    x -= 1f;
                }

                if (held[(int)InputActionId.MoveRight])
                {
                    x += 1f;
                }

                if (held[(int)InputActionId.MoveDown])
                {
                    y -= 1f;
                }

                if (held[(int)InputActionId.MoveUp])
                {
                    y += 1f;
                }

                var axis = new Vector2(x, y);
                return axis.sqrMagnitude > 1f ? axis.normalized : axis;
            }
        }

        /// <summary>平A 是否在本帧按下。</summary>
        public bool AttackPressed => WasPressed(InputActionId.Attack);

        /// <summary>闪避是否在本帧按下。</summary>
        public bool DodgePressed => WasPressed(InputActionId.Dodge);

        /// <summary>跳跃是否在本帧按下。</summary>
        public bool JumpPressed => WasPressed(InputActionId.Jump);

        /// <summary>暂停是否在本帧按下。</summary>
        public bool PausePressed => WasPressed(InputActionId.Pause);

        /// <summary>当前是否被界面压住了游戏输入（改键面板等）。</summary>
        public bool GameplayInputSuspended => gameplaySuspensions > 0;

        /// <summary>
        /// 暂时屏蔽游戏内动作（移动/攻击/技能/暂停）。给改键面板这类「按键只属于界面」的时刻用；
        /// 可多层压栈，每次 Push 都要有一次 Pop 对应。屏蔽期间所有边沿与按住状态都读成 false，
        /// 已经在按的动作会补发一次 Released，避免外层逻辑卡在「一直按住」。
        /// </summary>
        public void PushGameplaySuspension()
        {
            gameplaySuspensions++;
            if (gameplaySuspensions == 1)
            {
                ReleaseAllHeld();
            }
        }

        /// <summary>解除一层输入屏蔽，计数归零时再清一次残留状态。</summary>
        public void PopGameplaySuspension()
        {
            if (gameplaySuspensions <= 0)
            {
                return;
            }

            gameplaySuspensions--;
            if (gameplaySuspensions == 0)
            {
                ReleaseAllHeld();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 已经有跨场景的实例，这个副本不参与轮询。
                Destroy(this);
                return;
            }

            Instance = this;
            grid.Sanitize();
            if (persistAcrossScenes && transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }

            Bindings = settings != null ? settings.CreateTable() : new InputBindingTable();
            if (settings == null)
            {
                Debug.LogWarning("[Ring.Inputs] GameInput 没有指派 InputSettings，已使用文档默认键位。", this);
            }

            if (PersistsBindings)
            {
                LoadBindings();
            }

            MarkKeyCodesDirty();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (keyCodesDirty)
            {
                RebuildKeyCodes();
            }

            // 顺序固定：先按键轮询，再组装本帧意图，保证 Intent 里的边沿信号与
            // AttackPressed / SkillPressed 等属性读到的是同一帧状态。
            Poll();
            if (gameplaySuspensions > 0)
            {
                // 界面在吃按键（改键面板）：本帧意图清空，别让上一帧的移动/施法继续生效。
                intent = default;
                IntentUpdated?.Invoke(intent);
                return;
            }

            BuildIntent();
        }

        private void OnValidate()
        {
            grid.Sanitize();
            if (aimClickButton < 0 || aimClickButton > 2)
            {
                aimClickButton = 2;
            }
        }

        /// <summary>本帧是否按下。</summary>
        public bool WasPressed(InputActionId action)
        {
            return IsValid(action) && pressed[(int)action];
        }

        /// <summary>当前是否按住。</summary>
        public bool IsHeld(InputActionId action)
        {
            return IsValid(action) && held[(int)action];
        }

        /// <summary>本帧是否抬起。</summary>
        public bool WasReleased(InputActionId action)
        {
            return IsValid(action) && released[(int)action];
        }

        /// <summary>技能是否在本帧按下，tier 取 1~3。</summary>
        public bool SkillPressed(int tier)
        {
            InputActionId? action = InputActions.SkillActionOf(tier);
            return action.HasValue && WasPressed(action.Value);
        }

        /// <summary>某个动作当前绑定的键（调试/UI 用）。</summary>
        public string DescribeBinding(InputActionId action)
        {
            return new InputBindingEntry(action, Bindings.GetPrimary(action), Bindings.GetAlternate(action)).Describe();
        }

        /// <summary>取最近一次有效选点的世界坐标；无有效选点返回 false。</summary>
        public bool TryGetAimWorld(out Vector2 world)
        {
            world = aimWorldPoint;
            return aimValid;
        }

        /// <summary>取最近一次有效选点的格坐标；无有效选点返回 false。</summary>
        public bool TryGetAimCell(out GridCoord cell)
        {
            cell = aimCell;
            return aimValid;
        }

        /// <summary>取落在地图边界内的格坐标；越界或无有效选点都返回 false。</summary>
        public bool TryGetAimCellInGrid(out GridCoord cell)
        {
            cell = aimCell;
            return aimValid && aimInsideGrid;
        }

        /// <summary>世界坐标 → 格坐标。技能层复用同一套换算，避免各写一份 floor。</summary>
        public GridCoord WorldToCell(Vector2 world)
        {
            return grid.WorldToCell(world.x, world.y);
        }

        /// <summary>格坐标 → 格中心世界坐标（摆特效 / 高亮格）。</summary>
        public Vector2 CellToWorldCenter(GridCoord cell)
        {
            return grid.CellCenterWorld(cell);
        }

        /// <summary>替换整套格配置（切换地图 / 换擂台尺度时调用）。</summary>
        public void SetGrid(GridSpace space)
        {
            grid = space;
            grid.Sanitize();
        }

        /// <summary>
        /// 只平移格网原点：地图按擂台中心摆放时，由 MatchSpawner / 地图层把中心写进来。
        /// 刻意不改 CellSize 与边界，避免和 PlayerMotor.unitsPerCell 脱节。
        /// </summary>
        public void SetGridOrigin(float x, float y)
        {
            grid.OriginX = x;
            grid.OriginY = y;
        }

        public void SetGridOrigin(Vector2 origin)
        {
            SetGridOrigin(origin.x, origin.y);
        }

        /// <summary>运行时换相机（切场景 / 分屏）。</summary>
        public void SetWorldCamera(Camera cam)
        {
            worldCamera = cam;
        }

        /// <summary>
        /// 改键。若目标键已被别的动作占用则失败，并给出占用者。
        /// slot 取 0=主键、1=备用键。
        /// </summary>
        public bool TryRebind(InputActionId action, int slot, InputKey key, out InputActionId conflict)
        {
            conflict = default;
            if (!CanRebind)
            {
                Debug.LogWarning("[Ring.Inputs] 当前配置不允许运行时改键。", this);
                return false;
            }

            if (Bindings == null)
            {
                return false;
            }

            if (!Bindings.TrySetKey(action, slot, key, out conflict))
            {
                return false;
            }

            MarkKeyCodesDirty();
            if (PersistsBindings)
            {
                SaveBindings();
            }

            if (logRebinds)
            {
                Debug.Log("[Ring.Inputs] " + action.ToDisplayName() + " 的"
                          + (slot == 1 ? "备用键" : "主键") + " 改为 " + InputKeyCatalog.ToDisplayName(key), this);
            }

            return true;
        }

        /// <summary>恢复默认键位并清掉本地存档。</summary>
        public void ResetBindings()
        {
            lastLoadError = null;
            Bindings = settings != null ? settings.CreateTable() : new InputBindingTable();
            MarkKeyCodesDirty();

            if (PersistsBindings)
            {
                PlayerPrefs.DeleteKey(InputBindingSerializer.PlayerPrefsKey);
                PlayerPrefs.Save();
            }
        }

        /// <summary>把当前映射写入 PlayerPrefs。</summary>
        public void SaveBindings()
        {
            if (Bindings == null)
            {
                return;
            }

            PlayerPrefs.SetString(InputBindingSerializer.PlayerPrefsKey, InputBindingSerializer.Serialize(Bindings));
            PlayerPrefs.Save();
        }

        /// <summary>从 PlayerPrefs 读取；没有存档或解析失败时返回 false（保留当前映射）。</summary>
        public bool LoadBindings()
        {
            if (!PlayerPrefs.HasKey(InputBindingSerializer.PlayerPrefsKey))
            {
                return false;
            }

            string text = PlayerPrefs.GetString(InputBindingSerializer.PlayerPrefsKey, string.Empty);
            if (!InputBindingSerializer.TryDeserialize(text, out InputBindingTable loaded, out string error))
            {
                lastLoadError = error;
                Debug.LogWarning("[Ring.Inputs] 本地按键映射解析失败，已回退到默认键位：" + error, this);
                return false;
            }

            // 存档可能是改键面板误操作 / 手工改坏的：出现 Error 级问题（同一键绑给多个动作、
            // 关键动作没绑键）时直接丢弃并清档，回退到设计默认键位。
            // 否则会出现「W 被绑成跳跃、S 无绑定」这类串位，且现象与物理 bug 极难区分。
            var report = new ValidationReport();
            loaded.CollectIssues(report);
            if (report.HasErrors)
            {
                lastLoadError = "本地按键映射存在冲突或缺失，已回退到默认键位：" + report.ToText();
                Debug.LogWarning("[Ring.Inputs] " + lastLoadError, this);
                PlayerPrefs.DeleteKey(InputBindingSerializer.PlayerPrefsKey);
                PlayerPrefs.Save();
                return false;
            }

            lastLoadError = error;
            Bindings = loaded;
            MarkKeyCodesDirty();
            return true;
        }

        /// <summary>最近一次读档的问题（成功时也可能含「某行不认识」之类的小问题）。</summary>
        public string LastLoadError => lastLoadError;

        private string lastLoadError;

        /// <summary>收集运行时输入配置的问题，供自检/编辑器工具调用。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            if (settings == null)
            {
                report.Warn("输入系统", "GameInput 没有指派 InputSettings 资产，改键配置无法在设计期调整。");
            }
            else
            {
                settings.CollectIssues(report);
            }

            if (Bindings == null)
            {
                report.Error("输入系统", "映射表未初始化。");
                return;
            }

            Bindings.CollectIssues(report);
            InputKeyCatalog.CollectIssues(report);

            grid.CollectIssues(report);

            if (EffectiveCamera == null)
            {
                report.Warn("输入系统",
                    "鼠标选点没有可用相机：请把场景主相机拖进 GameInput 的 World Camera（或给它加 MainCamera 标签），否则 D12 选点一直无效。");
            }

            if (ignorePointerOverUI && EventSystem.current == null)
            {
                report.Info("输入系统",
                    "场景里没有 EventSystem，「忽略 UI 遮挡」不会生效；点 HUD 时也会更新选点。需要时请手工放一个 EventSystem。");
            }

            if (aimMode == AimMode.Click && aimClickButton == 0
                && Bindings.GetPrimary(InputActionId.Attack) == InputKey.MouseLeft)
            {
                report.Warn("输入系统",
                    "Click 模式用左键确认选点，而左键默认绑给平A，一次点击会同时触发两者；建议改用中键(2)或右键(1)。");
            }

            intent.CollectIssues(report);
        }

        /// <summary>
        /// 组装本帧 <see cref="InputIntent"/>：按键边沿信号 + D12 选点。
        /// 全部走旧版 UnityEngine.Input，不引用 com.unity.inputsystem。
        /// 将来换包时，本方法与 Poll() 就是唯一需要替换的轮询部分。
        /// </summary>
        private void BuildIntent()
        {
            Vector2 move = MoveAxis;

            int skillIndex = 0;
            for (int tier = 1; tier <= 3; tier++)
            {
                if (SkillPressed(tier))
                {
                    skillIndex = tier;
                    break;
                }
            }

            bool picked = false;
            if (aimMode == AimMode.Click)
            {
                // 只有确认键按下的那一帧才刷新；其余帧沿用已锁定的选点。
                if (UnityEngine.Input.GetMouseButtonDown(aimClickButton) && !BlockedByUI())
                {
                    picked = RefreshAimFromPointer();
                }
            }
            else if (BlockedByUI())
            {
                // Hover 模式下鼠标压在 HUD 上视为无有效选点，避免平A / 超我误用 HUD 背后的格子。
                aimValid = false;
            }
            else
            {
                RefreshAimFromPointer();
            }

            intent = new InputIntent
            {
                MoveX = move.x,
                MoveY = move.y,
                Jump = JumpPressed,
                BasicAttack = AttackPressed,
                Dodge = DodgePressed,
                SkillIndex = skillIndex,
                HasAimPoint = aimValid,
                AimWorldX = aimWorldPoint.x,
                AimWorldY = aimWorldPoint.y,
                AimCell = aimCell,
                AimInsideGrid = aimValid && aimInsideGrid,
            };

            IntentUpdated?.Invoke(intent);
            if (picked)
            {
                AimPicked?.Invoke(aimCell);
            }
        }

        /// <summary>
        /// 屏幕坐标 → 世界 XY → 格坐标，并刷新最近一次有效选点。
        /// 返回 true 表示本次投影成功（相机缺失 / 射线未命中时为 false）。
        /// </summary>
        private bool RefreshAimFromPointer()
        {
            Vector3 screen = UnityEngine.Input.mousePosition;
            if (!TryProjectToPlane(screen, EffectiveCamera, out Vector2 world))
            {
                aimValid = false;
                return false;
            }

            GridCoord cell = grid.WorldToCell(world.x, world.y);
            bool inside = grid.IsInside(cell);

            aimValid = true;
            aimWorldPoint = world;
            aimCell = cell;
            aimInsideGrid = inside;

            if (logAimPicks && (aimMode == AimMode.Click || !hasLoggedCell || !cell.Equals(lastLoggedCell)))
            {
                hasLoggedCell = true;
                lastLoggedCell = cell;
                Debug.Log("[输入/D12] 屏幕(" + screen.x.ToString("F0") + ", " + screen.y.ToString("F0")
                          + ") → 世界(" + world.x.ToString("F2") + ", " + world.y.ToString("F2")
                          + ") → 格" + cell + (inside ? string.Empty : "（地图外）"), this);
            }

            return true;
        }

        /// <summary>
        /// 把鼠标屏幕点投到 z = gamePlaneZ 的游戏平面。
        /// 用 ScreenPointToRay + Plane.Raycast，正交与透视相机都成立；
        /// 不用 ScreenToWorldPoint 硬塞 z 距离，避免透视相机下算错。
        /// </summary>
        private bool TryProjectToPlane(Vector3 screen, Camera cam, out Vector2 world)
        {
            world = default(Vector2);
            if (cam == null)
            {
                return false;
            }

            Ray ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, gamePlaneZ));
            if (!plane.Raycast(ray, out float enter) || enter <= 0f)
            {
                return false;
            }

            Vector3 hit = ray.GetPoint(enter);
            world = new Vector2(hit.x, hit.y);
            return true;
        }

        /// <summary>
        /// 鼠标是否压在 uGUI 上。旧版 Input 下用无参重载（对应鼠标指针）；
        /// 场景里没有 EventSystem 时返回 false，选点链不因此中断。
        /// </summary>
        private bool BlockedByUI()
        {
            return ignorePointerOverUI
                   && EventSystem.current != null
                   && EventSystem.current.IsPointerOverGameObject();
        }

        private void Poll()
        {
            if (gameplaySuspensions > 0)
            {
                // 界面正在吃按键（改键面板），这一帧不产生任何游戏动作。
                return;
            }

            for (int i = 0; i < InputActions.Count; i++)
            {
                bool anyHeld = false;
                bool anyDown = false;
                bool anyUp = false;

                for (int slot = 0; slot < InputBindingTable.SlotsPerAction; slot++)
                {
                    KeyCode code = keyCodes[i, slot];
                    if (code == KeyCode.None)
                    {
                        continue;
                    }

                    if (UnityEngine.Input.GetKey(code))
                    {
                        anyHeld = true;
                    }

                    if (UnityEngine.Input.GetKeyDown(code))
                    {
                        anyDown = true;
                    }

                    if (UnityEngine.Input.GetKeyUp(code))
                    {
                        anyUp = true;
                    }
                }

                held[i] = anyHeld;
                pressed[i] = anyDown;
                released[i] = anyUp;

                var action = (InputActionId)i;
                if (anyDown)
                {
                    if (logActionPresses)
                    {
                        // 只看得到「按了 1」却看不到「施放」，就能立刻判定问题在技能层而不是输入层。
                        Debug.Log("[输入] 按下 " + action.ToDisplayName()
                            + "（" + DescribeBinding(action) + "）", this);
                    }

                    ActionPressed?.Invoke(action);
                }

                if (anyUp)
                {
                    ActionReleased?.Invoke(action);
                }
            }
        }

        private void ReleaseAllHeld()
        {
            for (int i = 0; i < InputActions.Count; i++)
            {
                if (held[i])
                {
                    ActionReleased?.Invoke((InputActionId)i);
                }

                held[i] = false;
                pressed[i] = false;
                released[i] = false;
            }
        }

        private void MarkKeyCodesDirty()
        {
            keyCodesDirty = true;
        }

        private void RebuildKeyCodes()
        {
            for (int i = 0; i < InputActions.Count; i++)
            {
                var action = (InputActionId)i;
                keyCodes[i, 0] = Bindings != null ? InputKeyCatalog.ToKeyCode(Bindings.GetPrimary(action)) : KeyCode.None;
                keyCodes[i, 1] = Bindings != null ? InputKeyCatalog.ToKeyCode(Bindings.GetAlternate(action)) : KeyCode.None;
            }

            keyCodesDirty = false;
        }

        private static bool IsValid(InputActionId action)
        {
            return InputActions.IsDefined(action);
        }
    }
}
