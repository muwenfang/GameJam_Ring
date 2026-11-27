using System.Collections.Generic;
using Ring.Characters;
using Ring.Flow;
using Ring.Inputs;
using Ring.Maps;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ring.UI
{
    /// <summary>
    /// 界面层：只订阅 <see cref="GameFlow"/> 的状态事件，不碰钟声规则、不碰角色数值。
    ///
    /// 切换方式：单场景 + 面板切换，不换 Scene。
    /// 主菜单 Canvas、战斗画布、设置面板、HUD 都是场景里手工搭的，本组件只负责显示/隐藏与绑定按钮；
    /// 只有结算面板用 <see cref="UiBuilder"/> 运行时搭（autoBuildUi），HUD 找不到时才兜底搭一套。
    /// 不换 Scene 的理由：GameFlow / BellSystem / GameInput 与这些 Canvas 都挂在 SampleScene 的根物体上，
    /// 换场景就得把它们搬进 DontDestroyOnLoad，还要单独保住 SettingPanel —— 属于自找麻烦。
    ///
    /// 渲染层级（排查「开局后地图/角色不显示」）：地图与角色是 BattleMaps 下的世界空间 SpriteRenderer，
    /// 由 Main Camera 渲染；场景里 4 个 UI Canvas 全是 Screen Space - Overlay，永远画在相机之上。
    /// 因此任一「不透明 + 铺满全屏」的 Overlay Image 处于激活就会盖住整张战场。
    /// 对策：HideMenu 整块关掉主菜单 Canvas（含其全屏 MenuImage）；hideBattleBackdrops 关掉战斗画布里的
    /// 全屏纯色底图。TopCanvas 常驻激活且常驻置顶（TopCanvasSortingOrder），其下的顶层界面各自开合，
    /// 画布本身不随状态切换或设置面板开合而隐藏 / 还原层级。
    ///
    /// 界面构成：
    /// - 主菜单 MainMenu（场景里手工搭的 Canvas：开始游戏 / 设置 / 退出游戏）
    /// - 战斗画布 battleCanvas（场景里现名 BattleGround，开局后显示，并按本局随机地图切换场景）
    /// - 顶层画布 TopCanvas（场景里手工搭的 Canvas：常驻激活 + 常驻置顶，承载设置 / 结算 / 改键等顶层界面）
    /// - 设置面板 SettingPanel（挂在 TopCanvas 下，场景里存成激活，运行时由 HideSettings 收起）
    /// - HUD（场景里手工搭的 BattleHud，挂在 HudCanvas；找不到才运行时搭）
    /// - 结算（运行时搭：胜利 / 失败 + 返回主菜单）
    ///
    /// 场景约定（名字对上就能自动找到，也可以在 Inspector 里手动拖）：
    /// MainMenu / Start / Setting / Exit / SettingPanel（FullScreen / CloseSetting / AudioSlider）。
    /// 战斗画布按 "battleCanvas" 找，兼容场景里的旧名 "BattleGround"；顶层画布按 "TopCanvas" 找；
    /// 六个地图槽位挂在 BattleMapPresenter 上。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/界面 GameUiController")]
    public sealed class GameUiController : MonoBehaviour
    {
        private const string MainMenuCanvasName = "MainMenu";
        private const string SettingsPanelName = "SettingPanel";
        private const string StartButtonName = "Start";
        private const string SettingsButtonName = "Setting";
        private const string ExitButtonName = "Exit";
        private const string FullScreenButtonName = "FullScreen";
        private const string InputButtonName = "Input";
        private const string KeyBindingPanelName = "KeyBindingPanel";
        private const string CloseSettingsButtonName = "CloseSetting";
        private const string VolumeSliderName = "AudioSlider";
        private const string MenuBackdropName = "MenuImage";

        /// <summary>战斗画布在场景里的名字（大小写不敏感）。</summary>
        private const string BattleCanvasName = "battleCanvas";

        /// <summary>兼容场景里现有的旧名字，避免用户改名后接不上。</summary>
        private const string LegacyBattleCanvasName = "BattleGround";
        private const string SettingsBackdropName = "SettingImage";
        private const string CharacterRosterPath = "Assets/Data/Characters/CharacterRoster.asset";

        private const string FullScreenText = "全屏";
        private const string WindowedText = "退出全屏";

        private const string VolumeKey = "Ring.Volume";
        private const string FullScreenKey = "Ring.FullScreen";

        /// <summary>
        /// 顶层画布 TopCanvas 常驻的 Sorting Order。必须高于 HudCanvas(90)、RingCanvas(100)、
        /// 以及战斗画布 / 主菜单(0)，这样挂在它下面的设置、结算、改键等界面永远压在最上面。
        /// </summary>
        private const int TopCanvasSortingOrder = 1000;

        /// <summary>顶层画布在场景里的名字（大小写不敏感）。</summary>
        private const string TopCanvasName = "TopCanvas";

        [Header("主菜单（场景里手工搭的 Canvas）")]
        [SerializeField]
        [Tooltip("主菜单 Canvas；留空时按名字 \"" + MainMenuCanvasName + "\" 自动找。")]
        private Canvas mainMenuCanvas;

        [SerializeField]
        [Tooltip("设置面板（含全屏按钮）；留空时按名字自动找。")]
        private GameObject settingsPanel;

        [SerializeField]
        [Tooltip("开始游戏按钮；留空时按名字自动找。")]
        private Button startButton;

        [SerializeField]
        [Tooltip("退出游戏按钮；留空时按名字自动找。")]
        private Button exitButton;

        [SerializeField]
        [Tooltip("全屏/退出全屏按钮；留空时按名字自动找。")]
        private Button fullScreenButton;

        [SerializeField]
        [Tooltip("设置面板里的「键位调整」按钮（场景里名为 Input，挂在 TopCanvas/SettingPanel 下）；留空时按名字自动找。")]
        private Button inputButton;

        [SerializeField]
        [Tooltip("可选：游戏内键位调整面板，由用户在场景里手工搭建后拖入；留空时编辑器下打开菜单「钟声/输入映射/改键面板」。")]
        private GameObject keyBindingPanel;

        [SerializeField]
        [Tooltip("全屏按钮上的文字，会跟着全屏状态改；留空时取按钮下的第一个 Text。")]
        private Text fullScreenLabel;

        [SerializeField]
        [Tooltip("音量滑条；留空时按名字自动找。")]
        private Slider volumeSlider;

        [Header("战斗界面（场景里手工搭的 Canvas）")]
        [SerializeField]
        [Tooltip("开始游戏后显示的战斗画布；留空时按名字 \"" + BattleCanvasName + "\" 或 \"" + LegacyBattleCanvasName + "\" 自动找。")]
        private Canvas battleCanvas;

        [SerializeField]
        [Tooltip("六个地图槽位的展示器；留空时自动在场景里找。MapKit 六张地图根节点搭好前可以不挂。")]
        private BattleMapPresenter battleMapPresenter;

        [SerializeField]
        [Tooltip("可选：显示本局地图名与机制说明的 Text。场景里手工搭好后拖入；留空不影响运行，代码不自动创建 UI。")]
        private Text mapNameLabel;

        [SerializeField]
        [Tooltip("可选：显示当前地图机制状态的 Text（读取 MapKit 机制脚本的 StatusText）。留空不影响运行。")]
        private Text mapStatusLabel;

        [SerializeField]
        [Tooltip("战斗 HUD（左右血条 + 状态栏 + 钟声倒计时/回合数）。留空时先在场景里找，找不到再运行时搭一套。")]
        private BattleHud battleHud;

        [Header("顶层画布（场景里手工搭，常驻激活 + 置顶）")]
        [SerializeField]
        [Tooltip("顶层 UI 画布（场景里名为 TopCanvas）。运行时强制保持激活、并把 Sorting Order 设为 "
            + "TopCanvasSortingOrder，用于承载设置 / 结算 / 改键等需要盖住一切的界面；画布本身永不隐藏。"
            + "留空时按名字自动找。")]
        private Canvas topCanvas;

        [Header("行为")]
        [SerializeField]
        [Tooltip("游戏流程控制器；留空时自动取 GameFlow.Instance。")]
        private GameFlow gameFlow;

        [SerializeField]
        [Tooltip("是否在运行时自动搭建 HUD 与结算面板（主菜单用场景里手工搭的 Canvas，不再自动生成）。")]
        private bool autoBuildUi = true;

        [SerializeField]
        [Tooltip("编辑器里发现场景没有 MatchSpawner 时，运行时临时补一个（用角色总表资产）。打包后不会补，只报警告。")]
        private bool autoCreateSpawnerWhenMissing = true;

        [SerializeField]
        [Tooltip("在游戏里打开设置面板时暂停（Time.timeScale = 0），关掉就恢复。")]
        private bool pauseWhileSettingsOpen = true;

        [SerializeField]
        [Tooltip("运行时把 MenuImage / SettingImage 这类无图装饰 Image 的 Raycast Target 关掉，免得挡住按钮点击。")]
        private bool fixBlockingRaycasts = true;

        [SerializeField]
        [Tooltip("运行时关掉战斗画布里「没指定贴图 + 不透明 + 铺满全屏」的纯色占位底图（如 bg1）。"
            + "Screen Space - Overlay 会把这类 Image 画在世界空间的地图与角色之上；有贴图或半透明的不动。")]
        private bool hideBattleBackdrops = true;

        [SerializeField]
        [Tooltip("是否打印界面切换日志。")]
        private bool logUi;

        [SerializeField]
        [Tooltip("结算时是否仍显示战斗画布（结算面板是半透明的，能透出地图）。")]
        private bool keepBattleCanvasOnResult = true;

        [SerializeField]
        [Tooltip("是否把运行时搭的 HUD 叠在战斗画布上；只想用场景里手工搭的 UI 时关掉。")]
        private bool showHudWithBattleCanvas = true;

        [SerializeField]
        [Tooltip("结算（胜利 / 失败）时是否仍显示 HUD。默认关：局内（准备 / 收集 / 淘汰）HUD 一直显示。")]
        private bool keepHudOnResult;

        private GameObject hudRoot;
        private GameObject resultRoot;

        private Text resultTitle;
        private Text resultReason;

        /// <summary>主菜单子物体在 Awake 时的显示状态，回到主菜单时按它还原。</summary>
        private readonly Dictionary<GameObject, bool> menuChildStates = new Dictionary<GameObject, bool>();

        private bool settingsOpen;
        private bool pausedBySettings;
        private float timeScaleBeforePause = 1f;
        private float refreshTimer;

        private void Start()
        {
            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
            }

            if (gameFlow == null)
            {
                Debug.LogWarning("[界面] 场景里没有 GameFlow，界面不会响应任何操作。", this);
            }

            ResolveTopCanvas();
            ResolveMenuReferences();
            ResolveBattleReferences();

            if (autoBuildUi)
            {
                BuildGameplayUi();
            }

            if (gameFlow != null)
            {
                gameFlow.StateChanged += HandleStateChanged;
                gameFlow.MatchFinished += HandleMatchFinished;
                gameFlow.MapChanged += HandleMapChanged;
                gameFlow.ArenasChanged += HandleArenasChanged;
                gameFlow.GameInitialized += HandleGameInitialized;
                ApplyState(gameFlow.State);
            }
            else
            {
                ApplyState(MatchState.MainMenu);
            }
        }

        private void OnDestroy()
        {
            if (gameFlow != null)
            {
                gameFlow.StateChanged -= HandleStateChanged;
                gameFlow.MatchFinished -= HandleMatchFinished;
                gameFlow.MapChanged -= HandleMapChanged;
                gameFlow.ArenasChanged -= HandleArenasChanged;
                gameFlow.GameInitialized -= HandleGameInitialized;
            }

            // 暂停状态下被销毁（比如退出 Play），别把 Time.timeScale 留在 0。
            ClearPause();
        }

        private void Update()
        {
            HandleSettingsHotkey();

            if (gameFlow == null)
            {
                return;
            }

            MatchState state = gameFlow.State;
            if (state != MatchState.Collect && state != MatchState.Elimination)
            {
                return;
            }

            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                // HUD 的数字（倒计时、血条追赶）由 BattleHud 自己按帧刷新，这里只跟地图机制状态。
                refreshTimer = 0.1f;
                RefreshMapStatus();
            }
        }

        // ---------- 战斗画布与地图 ----------

        /// <summary>
        /// 把场景里的战斗画布与地图展示器找齐。刻意不自动创建任何 UI 物体 ——
        /// 画布由用户在场景里手工搭、拖进 Inspector；这里只做引用解析与显隐。
        /// </summary>
        private void ResolveBattleReferences()
        {
            if (battleCanvas == null)
            {
                battleCanvas = FindBattleCanvas();
            }

            if (battleMapPresenter == null)
            {
                BattleMapPresenter[] presenters = FindObjectsOfType<BattleMapPresenter>(true);
                if (presenters.Length > 0)
                {
                    battleMapPresenter = presenters[0];
                }
            }

            if (battleHud == null)
            {
                battleHud = FindObjectOfType<BattleHud>(true);
            }

            if (battleCanvas == null)
            {
                Debug.LogWarning(
                    "[界面] 找不到战斗画布（名字 \"" + BattleCanvasName + "\" 或 \"" + LegacyBattleCanvasName
                    + "\"）：开始游戏后不会切换战斗界面。请在 Inspector 里把 battleCanvas 拖进来。", this);
            }

            if (battleMapPresenter == null)
            {
                Debug.LogWarning(
                    "[界面] 场景里没有 BattleMapPresenter：本局仍会随机地图，但不展示地图场景"
                    + "（MapKit 六张地图根节点尚未搭进场景，这是预期状态）。接法见 Docs/06_MapKit地图资源与接线.md。", this);
            }

            // 战斗画布是 Screen Space - Overlay，会画在 Main Camera 的世界之上。
            // 画布里若有「无贴图 + 不透明 + 铺满全屏」的占位纯色底图，开局一显示就会盖住地图与角色。
            // 这类底图通常是搭界面时的临时背景（如 bg1），运行时关掉，避免污染战场。
            DisableBattleBackdrops();
        }

        /// <summary>
        /// 按名字找战斗画布：优先 "battleCanvas"，其次兼容场景里现有的旧名 "BattleGround"。
        /// 大小写不敏感；找不到返回 null，让调用方打警告而不是猜一个。
        /// </summary>
        private static Canvas FindBattleCanvas()
        {
            Canvas fallback = null;
            Canvas[] canvases = FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.rootCanvas != canvas)
                {
                    continue;
                }

                if (string.Equals(canvas.name, BattleCanvasName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return canvas;
                }

                if (fallback == null
                    && string.Equals(canvas.name, LegacyBattleCanvasName, System.StringComparison.OrdinalIgnoreCase))
                {
                    fallback = canvas;
                }
            }

            return fallback;
        }

        /// <summary>开局选定地图后：让展示器切到对应场景。ApplyState 里还有一次幂等兜底。</summary>
        private void HandleMapChanged(BattleMapId map)
        {
            if (battleMapPresenter != null)
            {
                battleMapPresenter.Show(map);
            }

            RefreshMapLabels(map);

            if (logUi)
            {
                Debug.Log("[界面] 本局地图：" + map.ToDisplayName()
                    + "（资源 " + map.ToZoneFolder() + "）", this);
            }
        }

        /// <summary>
        /// 刷新两个可选地图标签。两个都没拖时是空操作 —— 代码不创建、不查找任何 UI 物体。
        /// 地图名取 BattleMaps 映射表；状态文本经 BattleMapPresenter → MapScene 反射读 MapKit 机制。
        /// </summary>
        private void RefreshMapLabels(BattleMapId map)
        {
            if (mapNameLabel == null && mapStatusLabel == null)
            {
                return;
            }

            if (mapNameLabel != null)
            {
                BattleMapInfo info = BattleMaps.Info(map);
                mapNameLabel.text = info != null
                    ? info.DisplayName + "  ·  " + info.MechanicSummary
                    : "未知地图";
            }

            RefreshMapStatus();
        }

        /// <summary>只刷机制状态标签；Update 里按 0.1s 节流调用，避免每帧反射。</summary>
        private void RefreshMapStatus()
        {
            if (mapStatusLabel == null)
            {
                return;
            }

            string status = battleMapPresenter != null ? battleMapPresenter.StatusText() : string.Empty;
            mapStatusLabel.text = string.IsNullOrEmpty(status) ? "机制：待命" : "机制：" + status;
        }

        /// <summary>回主菜单 / 隐藏战斗画布时清空两个标签，避免残留上一局文字。</summary>
        private void ClearMapLabels()
        {
            if (mapNameLabel != null)
            {
                mapNameLabel.text = string.Empty;
            }

            if (mapStatusLabel != null)
            {
                mapStatusLabel.text = string.Empty;
            }
        }

        // ---------- 顶层画布（常驻激活 + 置顶） ----------

        /// <summary>
        /// 顶层画布是场景里手工搭的容器，用来放设置 / 结算 / 改键等需要盖住一切的界面。
        /// 约定：它自身永远激活、永远保持最高 Sorting Order；只有其下的子面板各自开合。
        /// 这样任何顶层界面一 SetActive(true) 就能画在最上面，不必再临时激活父 Canvas 或抬层级。
        /// </summary>
        private void ResolveTopCanvas()
        {
            if (topCanvas == null)
            {
                topCanvas = FindTopCanvas();
            }

            if (topCanvas == null)
            {
                Debug.LogWarning(
                    "[界面] 场景里找不到名为 " + TopCanvasName + " 的根 Canvas：顶层界面不会被置顶。"
                    + "请搭一个 Canvas 并拖进 Top Canvas 槽，或直接命名为 " + TopCanvasName + "。", this);
                return;
            }

            // 用户可能拖了子级 Canvas，统一收敛到根 Canvas，避免激活 / 置顶作用在半路节点上。
            if (topCanvas.rootCanvas != topCanvas)
            {
                topCanvas = topCanvas.rootCanvas;
            }

            EnsureTopCanvasOnTop();

            if (logUi)
            {
                Debug.Log("[界面] 顶层画布 " + topCanvas.name + "：常驻激活，Sorting Order = "
                    + TopCanvasSortingOrder + "。", this);
            }
        }

        /// <summary>按名字找顶层根 Canvas（含未激活物体）；找不到返回 null 让调用方打警告。</summary>
        private static Canvas FindTopCanvas()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.rootCanvas != canvas)
                {
                    continue;
                }

                if (string.Equals(canvas.name, TopCanvasName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return canvas;
                }
            }

            return null;
        }

        /// <summary>
        /// 幂等兜底：状态切换 / 打开顶层界面前调用，保证 TopCanvas 没被别处关掉或降层级。
        /// 只动画布本身，绝不碰它下面的子面板显隐。
        /// </summary>
        private void EnsureTopCanvasOnTop()
        {
            if (topCanvas == null)
            {
                return;
            }

            if (!topCanvas.gameObject.activeSelf)
            {
                topCanvas.gameObject.SetActive(true);
            }

            if (topCanvas.sortingOrder != TopCanvasSortingOrder)
            {
                topCanvas.sortingOrder = TopCanvasSortingOrder;
            }
        }

        // ---------- 场景里的主菜单 ----------

        /// <summary>把场景主菜单上的 Canvas / 按钮 / 滑条找齐，记下原始状态，绑好回调。</summary>
        private void ResolveMenuReferences()
        {
            if (mainMenuCanvas == null)
            {
                mainMenuCanvas = FindMenuCanvas();
            }

            if (mainMenuCanvas == null)
            {
                Debug.LogWarning(
                    "[界面] 场景里找不到名为 \"" + MainMenuCanvasName + "\" 的 Canvas（主菜单），菜单按钮不会响应。",
                    this);
                return;
            }

            Transform root = mainMenuCanvas.transform;
            if (settingsPanel == null)
            {
                settingsPanel = FindGameObject(root, SettingsPanelName);
            }

            // 设置面板实际挂在独立的顶层画布 TopCanvas 下，不在主菜单子树里；
            // 主菜单里找不到时再从顶层画布找一次（场景里通常已手工拖入 Inspector）。
            if (settingsPanel == null && topCanvas != null)
            {
                settingsPanel = FindGameObject(topCanvas.transform, SettingsPanelName);
            }

            if (startButton == null)
            {
                startButton = FindComponent<Button>(root, StartButtonName);
            }

            if (exitButton == null)
            {
                exitButton = FindComponent<Button>(root, ExitButtonName);
            }

            if (fullScreenButton == null)
            {
                fullScreenButton = FindComponent<Button>(root, FullScreenButtonName);
            }

            // 「键位调整」按钮挂在 TopCanvas/SettingPanel 下，不在 MainMenu Canvas 的子树里，
            // 所以先从设置面板找，再回退到主菜单与全场景（名字大小写不敏感）。
            if (inputButton == null)
            {
                inputButton = FindInputButton(root, settingsPanel);
            }

            if (keyBindingPanel == null)
            {
                keyBindingPanel = FindGameObjectAnywhere(KeyBindingPanelName);
            }

            if (volumeSlider == null)
            {
                volumeSlider = FindComponent<Slider>(root, VolumeSliderName);
            }

            if (fullScreenLabel == null && fullScreenButton != null)
            {
                fullScreenLabel = fullScreenButton.GetComponentInChildren<Text>(true);
            }

            if (settingsPanel == null)
            {
                Debug.LogWarning("[界面] 找不到设置面板 \"" + SettingsPanelName + "\"：设置/全屏按钮可能没有反应。", this);
            }

            SnapshotMenuChildren();
            BindMenuButtons();
            ApplySavedPlayerSettings();

            if (fixBlockingRaycasts)
            {
                DisableRaycast(FindComponent<Image>(root, MenuBackdropName), "它铺满全屏，会挡住主菜单按钮");
                DisableRaycast(FindComponent<Image>(root, SettingsBackdropName), "它在全屏按钮之前绘制，会挡住全屏按钮");
            }

            // 主菜单里的 MenuImage / 设置里的 SettingImage 都是「无贴图纯色」装饰层：
            // 只关它们的 Raycast Target（上面 fixBlockingRaycasts 做的事），保留其可见性做背景。
            // 主菜单整块 Canvas 会在 HideMenu 里关掉，所以这里不需要额外隐藏背景。

            // 场景里 SettingPanel 是存成开着的，运行时先收起来。
            HideSettings();
        }

        private static Canvas FindMenuCanvas()
        {
            Canvas[] canvases = FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.name != MainMenuCanvasName || canvas.rootCanvas != canvas)
                {
                    continue;
                }

                return canvas;
            }

            return null;
        }

        /// <summary>记下主菜单每个子物体原本是否显示；设置面板单独控制，不记。</summary>
        private void SnapshotMenuChildren()
        {
            menuChildStates.Clear();

            Transform root = mainMenuCanvas.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                GameObject child = root.GetChild(i).gameObject;
                if (settingsPanel != null && child == settingsPanel)
                {
                    continue;
                }

                menuChildStates[child] = child.activeSelf;
            }
        }

        private void BindMenuButtons()
        {
            Bind(startButton, "开始游戏", HandleStartClicked);
            Bind(exitButton, "退出游戏", HandleExitClicked);
            Bind(fullScreenButton, "全屏", HandleFullScreenClicked);
            Bind(inputButton, "键位调整", HandleInputClicked);
        }

        private void Bind(Button button, string label, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                Debug.LogWarning("[界面] 主菜单里找不到「" + label + "」按钮，这个功能不可用。", this);
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private void ShowMenu()
        {
            if (mainMenuCanvas == null)
            {
                return;
            }

            // 整块 Canvas 一起激活：主菜单是 Screen Space - Overlay，只还原子物体时
            // 万一 Canvas 本体被关过，子物体再怎么 SetActive(true) 也画不出来。
            mainMenuCanvas.gameObject.SetActive(true);

            foreach (KeyValuePair<GameObject, bool> pair in menuChildStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }

            HideSettings();
            RefreshFullScreenLabel();
        }

        private void HideMenu()
        {
            if (mainMenuCanvas == null)
            {
                return;
            }

            // 关键：整块关掉主菜单 Canvas，而不只是逐个关直接子物体。
            // 主菜单里 MenuImage 是「无贴图 + 不透明 + 铺满全屏」的纯色 Image；
            // 只要它残留（子物体快照漏一项、或某帧被重新激活），Overlay 层就会把
            // 世界空间里由 Main Camera 渲染的地图与角色整块盖住 —— 表现为「开局后看不到战场」。
            // 关掉 Canvas 本体最彻底：其下所有 UI（含射线挡板）一并失效。
            // 控制器 / GameFlow / 相机 / BattleMaps 都是场景根物体，不在这棵子树里，不受影响。
            foreach (GameObject child in menuChildStates.Keys)
            {
                if (child != null)
                {
                    child.SetActive(false);
                }
            }

            mainMenuCanvas.gameObject.SetActive(false);
            HideSettings();
        }

        // ---------- 主菜单按钮 ----------

        private void HandleStartClicked()
        {
            if (logUi)
            {
                Debug.Log("[界面] 点击开始游戏。", this);
            }

            if (gameFlow == null)
            {
                Debug.LogError("[界面] 场景里没有 GameFlow，无法开局。", this);
                return;
            }

            HideSettings();

            if (!gameFlow.State.CanStartMatch())
            {
                if (logUi)
                {
                    Debug.Log("[界面] 当前阶段是 " + gameFlow.State + "，忽略这次点击。", this);
                }

                return;
            }

            EnsureSpawnerAvailable();

            if (!gameFlow.StartMatch())
            {
                Debug.LogError(
                    "[界面] 开局失败，原因见上面【流程】/【生成】的日志。最常见的是场景里没有 MatchSpawner，"
                    + "或者它的「角色总表」是空的。",
                    this);
            }
        }

        private void HandleExitClicked()
        {
            if (logUi)
            {
                Debug.Log("[界面] 退出游戏。", this);
            }

#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 「退出游戏」按钮专用：Unity 的 OnClick 下拉只列出返回 void 的 public 无参方法，
        /// private 的 <see cref="HandleExitClicked"/> 选不到；此包装供 Inspector 直接拖绑。
        /// 若按钮已按名字「Exit」被运行时自动绑定，点击会触发两次退出 —— Quit 幂等，无副作用。
        /// </summary>
        public void ExitGameFromUi()
        {
            HandleExitClicked();
        }

        /// <summary>设置面板里的「键位调整」按钮回调（由代码按名字自动绑定）。</summary>
        private void HandleInputClicked()
        {
            OpenKeyBindings();
        }

        /// <summary>
        /// 「键位调整」按钮专用：Unity 的 OnClick 下拉只列出返回 void 的 public 无参方法，
        /// private 的 <see cref="HandleInputClicked"/> 选不到；此包装供 Inspector 手工拖绑。
        /// 注意：按钮平时已由代码按名字自动绑定，两种绑定方式二选一即可 ——
        /// 同时绑会让一次点击切换两次面板（打开后立刻关闭）。
        /// </summary>
        public void OpenKeyBindingsFromUi()
        {
            OpenKeyBindings();
        }

        private void OpenKeyBindings()
        {
            if (logUi)
            {
                Debug.Log("[界面] 点击键位调整。", this);
            }

            // 用户手搭的游戏内键位面板优先：有就切换显隐，代码不创建任何 UI 物体。
            if (keyBindingPanel != null)
            {
                keyBindingPanel.SetActive(!keyBindingPanel.activeSelf);
                return;
            }

#if UNITY_EDITOR
            // 面板还没搭时，退到工程自带的编辑器改键窗口（菜单「钟声/输入映射/改键面板」）。
            if (!EditorApplication.ExecuteMenuItem("钟声/输入映射/改键面板"))
            {
                Debug.LogWarning(
                    "[界面] 找不到菜单「钟声/输入映射/改键面板」。请把键位调整面板拖进 GameUiController 的 Key Binding Panel 槽。",
                    this);
            }
#else
            Debug.LogWarning(
                "[界面] 游戏内键位调整面板尚未搭建：请把面板拖进 GameUiController 的 Key Binding Panel 槽。",
                this);
#endif
        }

        private void HandleFullScreenClicked()
        {
            bool target = !Screen.fullScreen;
            Screen.fullScreen = target;
            PlayerPrefs.SetInt(FullScreenKey, target ? 1 : 0);

            RefreshFullScreenLabel();

            Debug.Log(target ? "[界面] 已切换到全屏。" : "[界面] 已退出全屏（窗口模式）。", this);
        }

        private void RefreshFullScreenLabel()
        {
            if (fullScreenLabel == null)
            {
                return;
            }

            fullScreenLabel.text = Screen.fullScreen ? WindowedText : FullScreenText;
        }

        private void HandleVolumeChanged(float value)
        {
            AudioListener.volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
        }

        private void ApplySavedPlayerSettings()
        {
            // 音量：滑条 → AudioListener.volume。没存过就沿用场景里滑条的初值，尊重原本的界面状态。
            float volume = PlayerPrefs.HasKey(VolumeKey)
                ? PlayerPrefs.GetFloat(VolumeKey, 1f)
                : (volumeSlider != null ? volumeSlider.value : 1f);
            AudioListener.volume = Mathf.Clamp01(volume);

            if (volumeSlider != null)
            {
                volumeSlider.onValueChanged.RemoveListener(HandleVolumeChanged);
                volumeSlider.onValueChanged.AddListener(HandleVolumeChanged);
                volumeSlider.SetValueWithoutNotify(AudioListener.volume);
            }

            // 全屏：打包后恢复上次的选择；编辑器里不动窗口，免得一按 Play 就全屏。
            if (!Application.isEditor && PlayerPrefs.GetInt(FullScreenKey, Screen.fullScreen ? 1 : 0) == 1)
            {
                Screen.fullScreen = true;
            }

            RefreshFullScreenLabel();
        }

        // ---------- 设置面板 ----------

        /// <summary>Esc / 暂停键：关着的则打开（主菜单除外），开着的则收起。</summary>
        private void HandleSettingsHotkey()
        {
            if (settingsPanel == null)
            {
                return;
            }

            GameInput input = GameInput.Instance;
            // 优先用输入映射里的「暂停」键；映射表里没配 Pause 时，Esc 也能用。
            bool pressed = (input != null && input.PausePressed) || Input.GetKeyDown(KeyCode.Escape);
            if (!pressed)
            {
                return;
            }

            if (settingsOpen)
            {
                HideSettings();
                return;
            }

            // 主菜单上按 Esc 不做任何事（那里本来就有「设置」按钮）。
            if (gameFlow != null && gameFlow.State == MatchState.MainMenu)
            {
                return;
            }

            ShowSettings();
        }

        private void ShowSettings()
        {
            if (settingsPanel == null)
            {
                return;
            }

            // 顶层画布常驻激活且已置顶，这里只需把面板自身打开；
            // 顺手幂等兜底一次，防止运行时有别处把 TopCanvas 关掉或降了层级。
            EnsureTopCanvasOnTop();
            settingsPanel.SetActive(true);
            settingsOpen = true;

            RefreshFullScreenLabel();
            ApplyPause();

            if (logUi)
            {
                Debug.Log("[界面] 打开设置面板。", this);
            }
        }

        private void HideSettings()
        {
            settingsOpen = false;

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }

            // 只收起面板自身。TopCanvas 是常驻置顶的顶层容器，不随设置面板开合而隐藏或还原层级，
            // 否则它下面的结算 / 改键等其它顶层界面会一起被关掉。

            ClearPause();

            if (logUi)
            {
                Debug.Log("[界面] 收起设置面板。", this);
            }
        }

        private void ApplyPause()
        {
            if (!pauseWhileSettingsOpen || gameFlow == null || !gameFlow.State.IsRunning() || pausedBySettings)
            {
                return;
            }

            pausedBySettings = true;
            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
        }

        private void ClearPause()
        {
            if (!pausedBySettings)
            {
                return;
            }

            pausedBySettings = false;
            Time.timeScale = timeScaleBeforePause;
        }

        // ---------- 运行时补 MatchSpawner ----------

        /// <summary>场景里没有 MatchSpawner 时：编辑器里临时补一个，打包后只报警告。</summary>
        private void EnsureSpawnerAvailable()
        {
            if (gameFlow == null || gameFlow.Spawner != null || FindObjectOfType<MatchSpawner>() != null)
            {
                return;
            }

#if UNITY_EDITOR
            if (autoCreateSpawnerWhenMissing)
            {
                CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(CharacterRosterPath);
                if (roster != null)
                {
                    var host = new GameObject("MatchSpawner（运行时临时）");
                    MatchSpawner spawner = host.AddComponent<MatchSpawner>();
                    spawner.Roster = roster;
                    Debug.LogWarning(
                        "[界面] 场景里没有 MatchSpawner，已在运行时临时补了一个（角色总表=" + CharacterRosterPath
                        + "）。建议把它正式加到场景里，或者关掉 GameUiController 的 autoCreateSpawnerWhenMissing。",
                        this);
                    return;
                }
            }
#endif

            Debug.LogWarning(
                "[界面] 场景里没有 MatchSpawner：开局会失败。请给一个空物体挂上 MatchSpawner，并指定角色总表。",
                this);
        }

        // ---------- 搭界面（HUD / 结算） ----------

        private void BuildGameplayUi()
        {
            // 结算面板仍旧由代码搭（纯占位，改的优先级低）。
            Canvas canvas = UiBuilder.CreateCanvas("RingCanvas");
            resultRoot = BuildResult(canvas.transform);

            // HUD：场景里手工搭的优先（拖进 Inspector 或在场景里找），都没有才运行时兜底搭一套。
            if (battleHud == null)
            {
                battleHud = FindObjectOfType<BattleHud>(true);
            }

            if (battleHud == null)
            {
                battleHud = BattleHudBuilder.Build(canvas.transform);
                Debug.Log(
                    "[界面] 场景里没有战斗 HUD，已运行时搭了一套默认布局。"
                    + "想自由改位置/配色的话，用菜单「钟声 / 界面 / 生成战斗 HUD（场景）」把它落成场景物体后再调。",
                    this);
            }

            hudRoot = battleHud.gameObject;
            hudRoot.SetActive(false);
            resultRoot.SetActive(false);
        }

        /// <summary>
        /// 血条、特性栏、倒计时、回合数都在 <see cref="BattleHud"/> 里；这里只在阶段切换时催它立刻重算一遍，
        /// 免得等它的 0.12 秒轮询。
        /// </summary>
        private void RefreshHud()
        {
            if (battleHud != null)
            {
                battleHud.RefreshNow();
            }
        }

        private GameObject BuildResult(Transform parent)
        {
            RectTransform panel = UiBuilder.CreateFullScreenPanel(parent, "Result", new Color(0f, 0f, 0f, 0.78f));

            resultTitle = UiBuilder.CreateText(
                panel, "ResultTitle", string.Empty, 120, UiBuilder.AccentColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1400f, 180f));

            resultReason = UiBuilder.CreateText(
                panel, "ResultReason", string.Empty, 34, new Color(0.85f, 0.86f, 0.9f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1400f, 140f));

            UiBuilder.CreateButton(
                panel, "再来一局", new Vector2(0.5f, 0.5f), new Vector2(-260f, -80f), new Vector2(420f, 100f),
                HandleRestartClicked, 40);

            UiBuilder.CreateButton(
                panel, "返回主菜单", new Vector2(0.5f, 0.5f), new Vector2(260f, -80f), new Vector2(420f, 100f),
                HandleReturnToMenuClicked, 40);

            return panel.gameObject;
        }

        private void HandleRestartClicked()
        {
            if (gameFlow == null)
            {
                return;
            }

            HideSettings();
            gameFlow.ReturnToMenu();
            // 需求：游戏结束后点击返回、重新开始游戏后需要 initializeGame。
            // 显式再调一次（幂等；ReturnToMenu 与 StartMatch 内部各保底调用一次），
            // 确保「返回 → 再来一局」这条路径的清场动作可见、可单独绑到按钮 OnClick。
            gameFlow.InitializeGame();
            HandleStartClicked();
        }

        private void HandleReturnToMenuClicked()
        {
            if (gameFlow == null)
            {
                return;
            }

            if (logUi)
            {
                Debug.Log("[界面] 返回主菜单。", this);
            }

            HideSettings();
            gameFlow.ReturnToMenu();
            // 点击「返回」即彻底清场，下一次点「开始游戏」时是一份全新的运行时状态。
            gameFlow.InitializeGame();
        }

        // ---------- 状态切换 ----------

        private void HandleStateChanged(MatchState from, MatchState to)
        {
            ApplyState(to);
        }

        private void HandleMatchFinished(MatchState result)
        {
            ApplyState(result);
        }

        /// <summary>位置钟声 / B8 重分配后，立刻刷新 HUD 里的「本场对手」与血条。</summary>
        private void HandleArenasChanged(ArenaPlan plan)
        {
            RefreshHud();
        }

        /// <summary>重开清场后让 HUD 丢掉上一局的数值（此时角色已销毁，刷新只会得到占位值）。</summary>
        private void HandleGameInitialized()
        {
            RefreshHud();
        }

        private void ApplyState(MatchState state)
        {
            // 顶层画布在所有状态下都保持激活 + 置顶；幂等兜底，不改变任何子面板。
            EnsureTopCanvasOnTop();

            bool menu = state == MatchState.MainMenu;
            bool running = state.IsRunning();
            bool finished = state.IsFinished();

            // 战斗画布：局内（准备/收集/淘汰）显示；结算时按开关决定是否保留（结算面板半透明，能透出地图）。
            bool showBattle = !menu && (running || (finished && keepBattleCanvasOnResult));

            // 面板切换：主菜单与战斗画布都是场景里的，HUD/结算挂在运行时新建的 Canvas 上。
            if (menu)
            {
                ShowMenu();
            }
            else
            {
                HideMenu();
            }

            if (battleCanvas != null)
            {
                battleCanvas.gameObject.SetActive(showBattle);
            }

            // 地图场景：局内按本局随机到的编号展示，回主菜单或隐藏战斗画布时收起。
            // Show 是幂等的：Loading → Collect 阶段切换会重复调用，不会反复触发 Enter。
            BattleMapId currentMapId = gameFlow != null ? gameFlow.CurrentMap : BattleMapId.Map01;
            if (battleMapPresenter != null)
            {
                if (showBattle)
                {
                    battleMapPresenter.Show(currentMapId);
                }
                else
                {
                    battleMapPresenter.Hide();
                }
            }

            if (showBattle)
            {
                RefreshMapLabels(currentMapId);
            }
            else
            {
                ClearMapLabels();
            }

            // HUD 在整个局内（准备 / 收集 / 淘汰）一直显示；结算时按开关决定。
            bool showHud = (running || (finished && keepHudOnResult)) && showHudWithBattleCanvas;
            if (hudRoot != null)
            {
                hudRoot.SetActive(showHud);
            }

            if (resultRoot != null)
            {
                resultRoot.SetActive(finished);
            }

            if (finished)
            {
                bool victory = state == MatchState.Victory;
                if (resultTitle != null)
                {
                    resultTitle.text = victory ? "胜  利" : "失  败";
                    resultTitle.color = victory ? UiBuilder.AccentColor : new Color(0.9f, 0.35f, 0.32f);
                }

                if (resultReason != null)
                {
                    string reason = gameFlow != null && gameFlow.Flow != null ? gameFlow.Flow.LastRejection : null;
                    resultReason.text = victory
                        ? "场上只剩你一个人。"
                        : ("你被淘汰了。" + (string.IsNullOrEmpty(reason) ? string.Empty : "\n" + reason));
                }
            }
            else if (running)
            {
                RefreshHud();
            }

            if (finished && keepHudOnResult)
            {
                RefreshHud();
            }

            if (logUi)
            {
                Debug.Log("[界面] 切到 " + state + "：主菜单 " + (menu ? "显示" : "隐藏")
                    + "，战斗画布 " + (showBattle ? "显示" : "隐藏")
                    + "，HUD " + (showHud ? "显示" : "隐藏")
                    + "，结算 " + (finished ? "显示" : "隐藏") + "。", this);
            }
        }

        // ---------- 小工具 ----------

        private void DisableRaycast(Image image, string reason)
        {
            if (image == null || !image.raycastTarget)
            {
                return;
            }

            image.raycastTarget = false;
            Debug.LogWarning(
                "[界面] 运行时把 " + image.name + " 的 Raycast Target 关掉了（" + reason + "）。建议在场景里直接取消勾选。",
                this);
        }

        /// <summary>
        /// 关掉战斗画布里「无贴图 + 不透明 + 铺满全屏」的纯色占位底图。
        /// 战斗画布是 Screen Space - Overlay，会画在 Main Camera 渲染的世界之上；
        /// 这种全屏纯色 Image 一旦显示，就会把地图与角色（世界空间 SpriteRenderer）整块盖住。
        /// 有美术贴图、半透明、或不是全屏拉伸的 Image 一律不动，避免误伤按钮与装饰。
        /// </summary>
        private void DisableBattleBackdrops()
        {
            if (!hideBattleBackdrops || battleCanvas == null)
            {
                return;
            }

            Image[] images = battleCanvas.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (!IsOpaqueFullScreenSolid(image))
                {
                    continue;
                }

                image.enabled = false;
                Debug.LogWarning(
                    "[界面] 运行时把战斗画布里的 " + image.name + " 关掉了：它是"
                    + "「无贴图 + 不透明 + 铺满全屏」的纯色 Image，Screen Space - Overlay 会把它画在"
                    + "世界空间的地图与角色之上，导致开局后看不到战场。建议直接在场景里取消该 Image 的勾选"
                    + "（或给它指定贴图 / 降低 alpha）；也可以在 GameUiController 上关掉 hideBattleBackdrops。",
                    this);
            }
        }

        /// <summary>判定：启用中、无贴图、不透明、且四边拉伸铺满父级（anchorMin 0,0 / anchorMax 1,1、无缩进）。</summary>
        private static bool IsOpaqueFullScreenSolid(Image image)
        {
            if (image == null || !image.enabled || image.sprite != null || image.color.a < 0.99f)
            {
                return false;
            }

            RectTransform rect = image.rectTransform;
            if (rect.anchorMin.sqrMagnitude > 0.0001f
                || (rect.anchorMax - Vector2.one).sqrMagnitude > 0.0001f)
            {
                return false;
            }

            // stretch 到父级且没有明显边距：真正的全屏铺底，而非某个定宽控件内部的填充。
            if (Mathf.Abs(rect.offsetMin.x) > 1f || Mathf.Abs(rect.offsetMin.y) > 1f
                || Mathf.Abs(rect.offsetMax.x) > 1f || Mathf.Abs(rect.offsetMax.y) > 1f)
            {
                return false;
            }

            return true;
        }

        private static GameObject FindGameObject(Transform root, string name)
        {
            Transform found = FindTransform(root, name);
            return found != null ? found.gameObject : null;
        }

        /// <summary>
        /// 找「键位调整」按钮：优先设置面板子级（TopCanvas/SettingPanel），再主菜单，最后全场景。
        /// 场景里按钮物体名为 "Input"、文字子物体名为 "input"，故统一大小写不敏感匹配。
        /// </summary>
        private static Button FindInputButton(Transform menuRoot, GameObject panel)
        {
            if (panel != null)
            {
                Button inPanel = FindButtonNamed(panel.transform, InputButtonName);
                if (inPanel != null)
                {
                    return inPanel;
                }
            }

            if (menuRoot != null)
            {
                Button inMenu = FindButtonNamed(menuRoot, InputButtonName);
                if (inMenu != null)
                {
                    return inMenu;
                }
            }

            Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null
                    && string.Equals(buttons[i].name, InputButtonName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return buttons[i];
                }
            }

            return null;
        }

        private static Button FindButtonNamed(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (string.Equals(buttons[i].name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return buttons[i];
                }
            }

            return null;
        }

        /// <summary>全场景按名字找 GameObject（含未激活物体）；给可选的手工面板留一个按名兜底。</summary>
        private static GameObject FindGameObjectAnywhere(string name)
        {
            Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null
                    && string.Equals(all[i].name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return all[i].gameObject;
                }
            }

            return null;
        }

        private static T FindComponent<T>(Transform root, string name) where T : Component
        {
            Transform found = FindTransform(root, name);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static Transform FindTransform(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransform(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
