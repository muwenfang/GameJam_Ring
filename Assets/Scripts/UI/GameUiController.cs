using System.Collections.Generic;
using System.Text;
using Ring.Abilities;
using Ring.Bells;
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
    /// 主菜单的 Canvas（含 SettingPanel）是场景里手工搭的，本组件只负责显示/隐藏与绑定按钮；
    /// HUD 与结算面板仍然是运行时用 <see cref="UiBuilder"/> 搭出来的（autoBuildUi）。
    /// 不换 Scene 的理由：GameFlow / BellSystem / GameInput 与主菜单 Canvas 都挂在 SampleScene 的根物体上，
    /// 换场景就得把它们搬进 DontDestroyOnLoad，还要单独保住 SettingPanel —— 属于自找麻烦。
    ///
    /// 界面构成：
    /// - 主菜单（场景里手工搭的 Canvas：开始游戏 / 设置 / 退出游戏）
    /// - 战斗画布 battleCanvas（场景里手工搭的 Canvas，开局后显示，并按本局随机地图切换场景）
    /// - HUD（运行时搭：阶段、下一次钟声倒计时、自己的牌与人；收集/淘汰只换提示文案）
    /// - 结算（运行时搭：胜利 / 失败 + 返回主菜单）
    ///
    /// 场景约定（名字对上就能自动找到，也可以在 Inspector 里手动拖）：
    /// MainMenu / Start / Setting / Exit / SettingPanel（FullScreen / CloseSetting / AudioSlider）。
    /// 战斗画布按 "battleCanvas" 找，兼容场景里的旧名 "BattleGround"；六个地图槽位挂在 BattleMapPresenter 上。
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

        /// <summary>设置面板打开时主菜单 Canvas 的层级：抬到 HUD（100）之上。</summary>
        private const int SettingsSortingOrder = 300;

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
        [Tooltip("六个地图槽位的展示器；留空时自动在场景里找。地图实体还没做时可以不挂。")]
        private BattleMapPresenter battleMapPresenter;

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
        [Tooltip("运行时把「没指定贴图、又不透明地铺在按钮上面」的装饰 Image 关掉（MenuImage / SettingImage）。指定了贴图就不动。")]
        private bool hideEmptyBackdrops = true;

        [SerializeField]
        [Tooltip("是否打印界面切换日志。")]
        private bool logUi;

        [SerializeField]
        [Tooltip("结算时是否仍显示战斗画布（结算面板是半透明的，能透出地图）。")]
        private bool keepBattleCanvasOnResult = true;

        [SerializeField]
        [Tooltip("是否把运行时搭的 HUD 叠在战斗画布上；只想用场景里手工搭的 UI 时关掉。")]
        private bool showHudWithBattleCanvas = true;

        private GameObject hudRoot;
        private GameObject resultRoot;

        private Text stateLabel;
        private Text timerLabel;
        private Text playerLabel;
        private Text rosterLabel;
        private Text resultTitle;
        private Text resultReason;

        /// <summary>主菜单子物体在 Awake 时的显示状态，回到主菜单时按它还原。</summary>
        private readonly Dictionary<GameObject, bool> menuChildStates = new Dictionary<GameObject, bool>();

        private int menuSortingOrder;
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
                refreshTimer = 0.1f;
                RefreshHud();
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
                    + "（六个地图实体尚未制作，这是预期状态）。接法见 Docs/05_战斗画布与地图接口.md。", this);
            }
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

            if (logUi)
            {
                Debug.Log("[界面] 本局地图：" + map.ToDisplayName(), this);
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

            menuSortingOrder = mainMenuCanvas.sortingOrder;

            Transform root = mainMenuCanvas.transform;
            if (settingsPanel == null)
            {
                settingsPanel = FindGameObject(root, SettingsPanelName);
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

            if (hideEmptyBackdrops)
            {
                HideEmptyBackdrop(
                    FindComponent<Image>(root, MenuBackdropName),
                    "它排在主菜单最后、又铺满全屏，会把开始/设置/退出三个按钮整块盖住");
                HideEmptyBackdrop(
                    FindComponent<Image>(root, SettingsBackdropName),
                    "它排在「全屏」按钮后面，会把全屏按钮整块盖住");
            }

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

            foreach (GameObject child in menuChildStates.Keys)
            {
                if (child != null)
                {
                    child.SetActive(false);
                }
            }

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

            settingsPanel.SetActive(true);
            settingsOpen = true;

            // 游戏里 HUD 的 Canvas 层级更高，设置面板要临时抬到它上面。
            if (mainMenuCanvas != null)
            {
                mainMenuCanvas.sortingOrder = SettingsSortingOrder;
            }

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

            if (mainMenuCanvas != null)
            {
                mainMenuCanvas.sortingOrder = menuSortingOrder;
            }

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
            Canvas canvas = UiBuilder.CreateCanvas("RingCanvas");
            hudRoot = BuildHud(canvas.transform);
            resultRoot = BuildResult(canvas.transform);

            hudRoot.SetActive(false);
            resultRoot.SetActive(false);
        }

        private GameObject BuildHud(Transform parent)
        {
            RectTransform panel = UiBuilder.CreateFullScreenPanel(parent, "Hud", new Color(0f, 0f, 0f, 0.18f));
            panel.GetComponent<Image>().raycastTarget = false;

            stateLabel = UiBuilder.CreateText(
                panel, "State", string.Empty, 34, Color.white, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(900f, 160f));
            UiBuilder.Place((RectTransform)stateLabel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, -24f), new Vector2(900f, 160f));
            stateLabel.alignment = TextAnchor.UpperLeft;

            timerLabel = UiBuilder.CreateText(
                panel, "Timer", string.Empty, 44, UiBuilder.AccentColor, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(900f, 80f));
            UiBuilder.Place((RectTransform)timerLabel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(900f, 80f));

            playerLabel = UiBuilder.CreateText(
                panel, "Player", string.Empty, 30, Color.white, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(1100f, 220f));
            UiBuilder.Place((RectTransform)playerLabel.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(32f, 28f), new Vector2(1100f, 220f));

            rosterLabel = UiBuilder.CreateText(
                panel, "Roster", string.Empty, 26, new Color(0.86f, 0.88f, 0.92f), TextAnchor.LowerRight,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(720f, 320f));
            UiBuilder.Place((RectTransform)rosterLabel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-32f, 28f), new Vector2(720f, 320f));

            return panel.gameObject;
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

        private void ApplyState(MatchState state)
        {
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
            if (battleMapPresenter != null)
            {
                if (showBattle)
                {
                    battleMapPresenter.Show(gameFlow != null ? gameFlow.CurrentMap : BattleMapId.Map01);
                }
                else
                {
                    battleMapPresenter.Hide();
                }
            }

            if (hudRoot != null)
            {
                hudRoot.SetActive(running && showHudWithBattleCanvas);
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

            if (logUi)
            {
                Debug.Log("[界面] 切到 " + state + "：主菜单 " + (menu ? "显示" : "隐藏")
                    + "，战斗画布 " + (showBattle ? "显示" : "隐藏")
                    + "，HUD " + (running && showHudWithBattleCanvas ? "显示" : "隐藏")
                    + "，结算 " + (finished ? "显示" : "隐藏") + "。", this);
            }
        }

        // ---------- HUD 刷新 ----------

        private void RefreshHud()
        {
            if (gameFlow == null || stateLabel == null)
            {
                return;
            }

            MatchState state = gameFlow.State;

            stateLabel.text = "阶段：" + state.ToDisplayName() + "\n" + state.ToHint();

            BellSystem bells = gameFlow.Bells;
            if (state == MatchState.Loading)
            {
                timerLabel.text = "准备中…";
            }
            else if (state == MatchState.Elimination || (bells != null && bells.Phase == BellPhase.Elimination))
            {
                timerLabel.text = "淘汰赛";
            }
            else if (bells != null)
            {
                timerLabel.text = "下一次钟声：" + Mathf.CeilToInt(Mathf.Max(0f, bells.SecondsUntilNextBell)) + " 秒";
            }
            else
            {
                timerLabel.text = "（没有钟声系统）";
            }

            CharacterInstance player = gameFlow.Player;
            if (player != null)
            {
                DerivedAbilityStats derived = player.Derived;
                var builder = new StringBuilder();
                builder.Append("你：").Append(player.DisplayName)
                    .Append("　生命 ").Append(Mathf.CeilToInt(player.CurrentHealth)).Append('/')
                    .Append(Mathf.CeilToInt(player.MaxHealth))
                    .Append("　防御 ").Append(Mathf.RoundToInt(derived.Defense))
                    .Append("（减伤 ").Append(AbilityMath.Percent(derived.DamageReduction)).Append("）")
                    .Append("　移速 ").Append(AbilityMath.Number(derived.MoveSpeedCellsPerSecond)).Append(" 格/秒");
                builder.Append('\n').Append(DescribeCards(player.Participant));
                builder.Append('\n').Append(DescribeAbilities(player));
                if (player.Abilities != null)
                {
                    builder.Append('\n').Append(player.Abilities.DescribeState());
                }

                playerLabel.text = builder.ToString();
            }
            else
            {
                playerLabel.text = string.Empty;
            }

            rosterLabel.text = DescribeRoster();
        }

        private string DescribeRoster()
        {
            MatchSpawner spawner = gameFlow != null ? gameFlow.Spawner : null;
            if (spawner == null || spawner.Instances.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder("场上：");
            int alive = 0;
            for (int i = 0; i < spawner.Instances.Count; i++)
            {
                CharacterInstance character = spawner.Instances[i];
                if (character == null)
                {
                    continue;
                }

                bool inPlay = character.Participant == null || character.Participant.IsInPlay;
                if (inPlay && !character.IsDead)
                {
                    alive++;
                }

                builder.Append('\n').Append(inPlay ? "· " : "× ").Append(character.DisplayName);
                if (character.HasCompleteSet)
                {
                    builder.Append(" ★");
                }
            }

            builder.Append("\n在场 ").Append(alive).Append(" 人");
            return builder.ToString();
        }

        /// <summary>五个能力槽：特质（被动）、普攻、本我/自我/超我，带冷却状态。</summary>
        private static string DescribeAbilities(CharacterInstance character)
        {
            if (character == null || character.Loadout == null || character.Loadout.IsEmpty)
            {
                return "能力：—";
            }

            var builder = new StringBuilder("能力：");
            builder.Append(character.Loadout.Describe());

            IReadOnlyList<AbilityCard> cards = character.Loadout.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCard card = cards[i];
                AbilityDefinition definition = card.Definition;
                builder.Append('\n').Append("　").Append(card.Slot.ToDisplayName()).Append('：');
                builder.Append(definition != null ? definition.AbilityName : "（缺少数值）");

                if (card.Slot.IsBasicAttack())
                {
                    builder.Append("　伤害 ").Append(AbilityMath.Number(character.Derived.BasicAttackDamage))
                        .Append("　间隔 ").Append(AbilityMath.Number(character.Derived.AttackIntervalSeconds)).Append("s");
                }
                else if (card.Slot.IsSkill())
                {
                    float cooldown = character.Abilities != null ? character.Abilities.GetCooldownRemaining(card.Slot) : 0f;
                    builder.Append(cooldown > 0f
                        ? "　冷却 " + Mathf.CeilToInt(cooldown) + "s"
                        : "　就绪");
                }
                else
                {
                    builder.Append("　被动");
                }
            }

            return builder.ToString();
        }

        private static string DescribeCards(IBellParticipant participant)
        {
            if (participant == null)
            {
                return "牌：—";
            }

            var builder = new StringBuilder("牌：");
            if (participant.Traits.Count == 0)
            {
                builder.Append("（无特质）");
            }
            else
            {
                for (int i = 0; i < participant.Traits.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("、");
                    }

                    builder.Append(participant.Traits[i].ToDisplayName());
                }
            }

            builder.Append("　技能 ").Append(participant.Skills.Count).Append("/3");
            if (BellCards.HasCompleteSet(participant.Traits, participant.Skills))
            {
                builder.Append("　★已集齐");
            }

            return builder.ToString();
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
        /// 没指定贴图的 Image 画出来就是一块纯色。这类装饰层如果铺在按钮上面，按钮就"看不见但还能点"，
        /// 排错时很费劲，所以运行时先关掉并提示。指定了贴图（或半透明）就不动。
        /// </summary>
        private void HideEmptyBackdrop(Image image, string reason)
        {
            if (image == null || !image.enabled || image.sprite != null || image.color.a < 0.99f)
            {
                return;
            }

            image.enabled = false;
            Debug.LogWarning(
                "[界面] 运行时把 " + image.name + " 关掉了（" + reason + "）。它是没有贴图的纯色 Image；"
                + "要么给它指定贴图，要么把它排到按钮前面，要么在场景里直接关掉这个 Image，"
                + "也可以在 GameUiController 上关掉 hideEmptyBackdrops。",
                this);
        }

        private static GameObject FindGameObject(Transform root, string name)
        {
            Transform found = FindTransform(root, name);
            return found != null ? found.gameObject : null;
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
