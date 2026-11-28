using System.Collections.Generic;
using Ring.Inputs;
using UnityEngine;
using UnityEngine.UI;

namespace Ring.UI
{
    /// <summary>
    /// 游戏内「键位调整」面板 —— 给玩家用的，不是开发者工具。
    ///
    /// 一行一个动作，左边是动作名，右边「主键 / 备用键」两个按钮；点一下按钮进入捕捉态，
    /// 再按下想用的键就改好（鼠标键也能绑）。改键走 <see cref="GameInput.TryRebind"/>，
    /// 成功后会按 InputSettings 的配置自动落盘（PlayerPrefs）。
    ///
    /// 界面全部由代码搭建（见 <see cref="UiBuilder"/>），不依赖场景里的预制体；
    /// 挂在 TopCanvas 下，天然盖住战斗界面。打开时暂停，关闭时还原 —— 与设置面板的暂停可以嵌套。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/界面 KeyBindingPanel")]
    public sealed class KeyBindingPanel : MonoBehaviour
    {
        /// <summary>运行时面板的物体名；GameUiController 也按这个名字找。</summary>
        public const string PanelName = "KeyBindingPanel";

        private const float PanelWidth = 1000f;
        private const float PanelHeight = 960f;
        private const float RowHeight = 44f;
        private const float RowGap = 8f;
        private const float RowTop = 258f;
        private const float LabelWidth = 340f;
        private const float KeyWidth = 260f;
        private const float LabelLeft = -460f;
        private const float PrimaryLeft = -100f;
        private const float AlternateLeft = 180f;

        private static readonly Color NormalColor = new Color(0.78f, 0.81f, 0.86f, 1f);
        private static readonly Color SuccessColor = new Color(0.55f, 0.90f, 0.62f, 1f);
        private static readonly Color ErrorColor = new Color(0.98f, 0.52f, 0.45f, 1f);

        private readonly Text[] actionLabels = new Text[InputActions.Count];
        private readonly Text[] primaryLabels = new Text[InputActions.Count];
        private readonly Text[] alternateLabels = new Text[InputActions.Count];
        private readonly Button[] primaryButtons = new Button[InputActions.Count];
        private readonly Button[] alternateButtons = new Button[InputActions.Count];

        private GameInput input;
        private Text statusText;
        private bool built;
        private bool isOpen;
        private bool capturing;
        private InputActionId captureAction;
        private int captureSlot;
        private bool pausedByPanel;
        private float timeScaleBeforePause = 1f;
        private bool inputSuspended;

        /// <summary>面板是否开着。</summary>
        public bool IsOpen => isOpen;

        /// <summary>是否正在等玩家按键。</summary>
        public bool IsCapturing => capturing;

        /// <summary>运行时搭一个面板并挂到 parent 下（初始隐藏）。</summary>
        public static KeyBindingPanel Create(Transform parent)
        {
            var host = new GameObject(PanelName, typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var panel = host.AddComponent<KeyBindingPanel>();
            host.SetActive(false);
            return panel;
        }

        private void Awake()
        {
            EnsureBuilt();
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // 面板被别处关掉时也别把 Time.timeScale 留在 0，也别继续等按键、别一直压着游戏输入。
            capturing = false;
            isOpen = false;
            ResumePause();
            ResumeGameplayInput();
        }

        // ---------- 对外 ----------

        /// <summary>打开面板（暂停 + 刷新键位显示）。</summary>
        public void Open()
        {
            EnsureBuilt();

            if (input == null)
            {
                input = GameInput.Instance;
            }

            isOpen = true;
            capturing = false;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            ApplyPause();
            SuspendGameplayInput();
            RefreshRows();

            if (input == null)
            {
                SetStatus("场景里没有 GameInput，暂时无法改键。", ErrorColor);
            }
            else if (!input.CanRebind)
            {
                SetStatus("当前输入配置不允许玩家改键（InputSettings.allowRuntimeRebinding 关着）。", ErrorColor);
            }
            else
            {
                SetStatus("点一下键位，再按下你想用的键；Esc 取消。", NormalColor);
            }
        }

        /// <summary>关闭面板（还原暂停与输入）。</summary>
        public void Close()
        {
            capturing = false;
            isOpen = false;
            gameObject.SetActive(false);
            ResumePause();
            ResumeGameplayInput();
        }

        /// <summary>
        /// 每帧调用：面板开着时吃掉 Esc / 捕捉用按键。
        /// 返回 true 表示这一帧的输入归面板所有，调用方不要再处理 Esc。
        /// </summary>
        public bool TickInput()
        {
            if (!isOpen)
            {
                return false;
            }

            // Esc 优先当「取消捕捉 / 关闭面板」，所以不能让玩家把 Esc 绑给别的动作。
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (capturing)
                {
                    CancelCapture();
                }
                else
                {
                    Close();
                }

                return true;
            }

            if (!capturing)
            {
                return false;
            }

            // 鼠标键：改键面板自己的点击会先被 uGUI 吃掉，这里只认「按下瞬间」。
            for (int button = 0; button < 3; button++)
            {
                if (!Input.GetMouseButtonDown(button))
                {
                    continue;
                }

                InputKey mouseKey = button == 0
                    ? InputKey.MouseLeft
                    : button == 1 ? InputKey.MouseRight : InputKey.MouseMiddle;
                ApplyKey(mouseKey);
                return true;
            }

            IReadOnlyList<InputKey> keys = InputKeyCatalog.All;
            for (int i = 0; i < keys.Count; i++)
            {
                InputKey key = keys[i];
                if (InputKeyCatalog.IsMouseButton(key))
                {
                    continue;
                }

                KeyCode code = InputKeyCatalog.ToKeyCode(key);
                if (code != KeyCode.None && Input.GetKeyDown(code))
                {
                    ApplyKey(key);
                    return true;
                }
            }

            return false;
        }

        // ---------- 搭界面 ----------

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            built = true;
            UiBuilder.Stretch((RectTransform)transform);

            // 压暗背景挡住下面的点击 —— 面板开着时不该点到底下的按钮。
            UiBuilder.CreateFullScreenPanel(transform, "Dim", new Color(0f, 0f, 0f, 0.66f));

            RectTransform box = UiBuilder.CreateRect(transform, "Box");
            UiBuilder.Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var background = box.gameObject.AddComponent<Image>();
            background.color = UiBuilder.PanelColor;

            UiBuilder.CreateText(
                box, "Title", "键位调整", 44, UiBuilder.AccentColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 412f), new Vector2(900f, 56f));

            UiBuilder.CreateText(
                box, "Hint", "点一下键位，再按下你想用的键。Esc 取消。改完立刻生效，并自动保存。", 24, NormalColor,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 358f), new Vector2(900f, 36f));

            const float headerY = 306f;
            CreateHeader(box, "动作", LabelLeft + LabelWidth * 0.5f, headerY);
            CreateHeader(box, "主键", PrimaryLeft + KeyWidth * 0.5f, headerY);
            CreateHeader(box, "备用键", AlternateLeft + KeyWidth * 0.5f, headerY);

            for (int i = 0; i < InputActions.Count; i++)
            {
                InputActionId action = InputActions.All[i];
                float y = RowTop - i * (RowHeight + RowGap);

                Text label = UiBuilder.CreateText(
                    box, "Action_" + action, action.ToDisplayName(), 26, Color.white, TextAnchor.MiddleLeft,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(LabelWidth, RowHeight));
                UiBuilder.Place((RectTransform)label.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(LabelLeft, y), new Vector2(LabelWidth, RowHeight));
                actionLabels[i] = label;

                primaryButtons[i] = CreateKeyButton(box, action, 0, new Vector2(PrimaryLeft, y), out primaryLabels[i]);
                alternateButtons[i] = CreateKeyButton(box, action, 1, new Vector2(AlternateLeft, y), out alternateLabels[i]);
            }

            statusText = UiBuilder.CreateText(
                box, "Status", string.Empty, 24, NormalColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -318f), new Vector2(900f, 64f));

            UiBuilder.CreateButton(box, "恢复默认", new Vector2(0.5f, 0.5f), new Vector2(-170f, -404f),
                new Vector2(300f, 64f), ResetAll, 28);
            UiBuilder.CreateButton(box, "关闭", new Vector2(0.5f, 0.5f), new Vector2(170f, -404f),
                new Vector2(300f, 64f), Close, 28);

            RefreshRows();
        }

        private static void CreateHeader(Transform parent, string text, float centerX, float y)
        {
            UiBuilder.CreateText(
                parent, "Header_" + text, text, 24, new Color(0.62f, 0.67f, 0.76f, 1f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(centerX, y), new Vector2(KeyWidth, 32f));
        }

        private Button CreateKeyButton(Transform parent, InputActionId action, int slot, Vector2 position, out Text label)
        {
            // 先用占位文字建按钮，随后的 RefreshRows 会把它换成真实键位。
            Button button = UiBuilder.CreateButton(
                parent, slot == 0 ? "主键" : "备用键", new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(KeyWidth, RowHeight), null, 24);

            UiBuilder.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                position, new Vector2(KeyWidth, RowHeight));

            label = button.GetComponentInChildren<Text>(true);

            InputActionId capturedAction = action;
            int capturedSlot = slot;
            button.onClick.AddListener(() => BeginCapture(capturedAction, capturedSlot));
            return button;
        }

        // ---------- 改键流程 ----------

        private void BeginCapture(InputActionId action, int slot)
        {
            if (input == null)
            {
                input = GameInput.Instance;
            }

            if (input == null)
            {
                SetStatus("场景里没有 GameInput，改不了键。", ErrorColor);
                return;
            }

            if (!input.CanRebind)
            {
                SetStatus("当前输入配置不允许玩家改键。", ErrorColor);
                return;
            }

            capturing = true;
            captureAction = action;
            captureSlot = slot;
            SetStatus("按下「" + action.ToDisplayName() + "」新的" + SlotName(slot) + "；Esc 取消。", UiBuilder.AccentColor);
            RefreshRows();
        }

        private void CancelCapture()
        {
            capturing = false;
            SetStatus("已取消改键。", NormalColor);
            RefreshRows();
        }

        private void ApplyKey(InputKey key)
        {
            InputActionId action = captureAction;
            int slot = captureSlot;
            capturing = false;

            if (input == null || key == InputKey.None)
            {
                RefreshRows();
                return;
            }

            InputKey current = input.Bindings.GetKey(action, slot);
            if (key == current)
            {
                SetStatus("「" + action.ToDisplayName() + "」" + SlotName(slot) + "本来就是「"
                          + InputKeyCatalog.ToDisplayName(key) + "」。", NormalColor);
                RefreshRows();
                return;
            }

            if (!input.TryRebind(action, slot, key, out InputActionId conflict))
            {
                if (!input.CanRebind)
                {
                    SetStatus("当前输入配置不允许玩家改键。", ErrorColor);
                }
                else
                {
                    SetStatus("「" + InputKeyCatalog.ToDisplayName(key) + "」已经被「"
                              + conflict.ToDisplayName() + "」占用了，换一个键。", ErrorColor);
                }

                RefreshRows();
                return;
            }

            string saved = input.PersistsBindings ? "，已保存。" : "（当前配置不落盘，重开游戏会还原）。";
            SetStatus("「" + action.ToDisplayName() + "」" + SlotName(slot) + "已改为「"
                      + InputKeyCatalog.ToDisplayName(key) + "」" + saved, SuccessColor);
            RefreshRows();
        }

        private void ResetAll()
        {
            if (input == null)
            {
                input = GameInput.Instance;
            }

            if (input == null)
            {
                SetStatus("场景里没有 GameInput，改不了键。", ErrorColor);
                return;
            }

            if (!input.CanRebind)
            {
                SetStatus("当前输入配置不允许玩家改键。", ErrorColor);
                return;
            }

            capturing = false;
            input.ResetBindings();
            SetStatus("已恢复文档默认键位。", SuccessColor);
            RefreshRows();
        }

        // ---------- 刷新与暂停 ----------

        private void RefreshRows()
        {
            if (!built)
            {
                return;
            }

            bool canRebind = input != null && input.CanRebind;

            for (int i = 0; i < InputActions.Count; i++)
            {
                InputActionId action = InputActions.All[i];
                actionLabels[i].text = action.ToDisplayName();

                bool hasInput = input != null;
                InputKey primary = hasInput ? input.Bindings.GetKey(action, 0) : InputKey.None;
                InputKey alternate = hasInput ? input.Bindings.GetKey(action, 1) : InputKey.None;

                bool capturingPrimary = capturing && captureAction == action && captureSlot == 0;
                bool capturingAlternate = capturing && captureAction == action && captureSlot == 1;

                primaryLabels[i].text = capturingPrimary ? "按下新键…" : DescribeKey(primary);
                alternateLabels[i].text = capturingAlternate ? "按下新键…" : DescribeKey(alternate);
                primaryLabels[i].color = capturingPrimary ? UiBuilder.AccentColor : Color.white;
                alternateLabels[i].color = capturingAlternate ? UiBuilder.AccentColor : Color.white;

                if (primaryButtons[i] != null)
                {
                    primaryButtons[i].interactable = canRebind;
                }

                if (alternateButtons[i] != null)
                {
                    alternateButtons[i].interactable = canRebind;
                }
            }
        }

        private static string DescribeKey(InputKey key)
        {
            return key == InputKey.None ? "—" : InputKeyCatalog.ToDisplayName(key);
        }

        private static string SlotName(int slot)
        {
            return slot == 0 ? "主键" : "备用键";
        }

        private void SetStatus(string message, Color color)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = message;
            statusText.color = color;
        }

        private void ApplyPause()
        {
            if (pausedByPanel)
            {
                return;
            }

            // 记下进入时的 timeScale 再设 0：设置面板可能已经暂停过一次，
            // 那种情况下记下的是 0，关闭时还原成 0，等设置面板关掉再还原成 1。
            pausedByPanel = true;
            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
        }

        private void ResumePause()
        {
            if (!pausedByPanel)
            {
                return;
            }

            pausedByPanel = false;
            Time.timeScale = timeScaleBeforePause;
        }

        /// <summary>面板开着时，按键只属于这里，不要顺带触发行走和技能。</summary>
        private void SuspendGameplayInput()
        {
            if (inputSuspended)
            {
                return;
            }

            var source = input != null ? input : GameInput.Instance;
            if (source == null)
            {
                return;
            }

            input = source;
            input.PushGameplaySuspension();
            inputSuspended = true;
        }

        private void ResumeGameplayInput()
        {
            if (!inputSuspended)
            {
                return;
            }

            inputSuspended = false;
            if (input != null)
            {
                input.PopGameplaySuspension();
            }
        }
    }
}
