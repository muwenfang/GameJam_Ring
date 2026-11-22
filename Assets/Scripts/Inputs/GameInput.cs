using System;
using Ring.Core;
using UnityEngine;

namespace Ring.Inputs
{
    /// <summary>
    /// 输入系统的运行时入口：持有当前映射表，每帧轮询旧版 Input，向外提供
    /// 「按下 / 按住 / 抬起」三种状态和改键 API。
    ///
    /// 之所以用旧版 InputManager 而不是新 Input System：
    /// 工程当前没有安装 com.unity.inputsystem（见 Packages/manifest.json），
    /// 为了不给团队加依赖，这里直接用 UnityEngine.Input；映射表本身是纯数据，
    /// 将来换成新系统只需要改本文件的轮询部分。
    /// </summary>
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

        private readonly bool[] pressed = new bool[InputActions.Count];
        private readonly bool[] held = new bool[InputActions.Count];
        private readonly bool[] released = new bool[InputActions.Count];

        // 把枚举先转成 KeyCode 缓存起来，避免每帧对每个动作做字符串解析。
        private readonly KeyCode[,] keyCodes = new KeyCode[InputActions.Count, InputBindingTable.SlotsPerAction];

        private bool keyCodesDirty = true;

        /// <summary>某个动作被按下时触发（每帧最多一次）。</summary>
        public event Action<InputActionId> ActionPressed;

        /// <summary>某个动作被抬起时触发。</summary>
        public event Action<InputActionId> ActionReleased;

        /// <summary>当前映射表。</summary>
        public InputBindingTable Bindings { get; private set; }

        /// <summary>当前输入配置资产（可能为空）。</summary>
        public InputSettings Settings => settings;

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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 已经有跨场景的实例，这个副本不参与轮询。
                Destroy(this);
                return;
            }

            Instance = this;
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

            Poll();
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
        }

        private void Poll()
        {
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
                    ActionPressed?.Invoke(action);
                }

                if (anyUp)
                {
                    ActionReleased?.Invoke(action);
                }
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
