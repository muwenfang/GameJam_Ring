using System.Text;
using UnityEditor;
using UnityEngine;
using Ring.Core;
using Ring.Inputs;

namespace Ring.EditorTools
{
    /// <summary>
    /// 改键面板：把「捕捉到的按键」写进 InputSettings 资产。
    ///
    /// 放在 Editor 里而不是游戏内 UI，是因为键位属于策划配置；
    /// 运行时改键（GameInput.TryRebind + PlayerPrefs）走的是另一条路，两者共用同一张映射表。
    /// </summary>
    public sealed class InputRebindWindow : EditorWindow
    {
        private InputSettings settings;
        private InputBindingTable table;
        private InputActionId? capturingAction;
        private int capturingSlot;
        private string status;
        private bool dirty;

        [MenuItem("钟声/输入映射/改键面板", false, 22)]
        public static void Open()
        {
            var window = GetWindow<InputRebindWindow>(false, "输入映射", true);
            window.minSize = new Vector2(560f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            Reload();
        }

        private void Reload()
        {
            settings = AssetDatabase.LoadAssetAtPath<InputSettings>(RingTools.InputSettingsPath);
            if (settings != null)
            {
                table = settings.CreateTable();
                status = null;
            }
            else
            {
                table = new InputBindingTable();
                table.ResetToDefaults();
                status = "找不到 InputSettings.asset，当前只是临时改键，无法保存。";
            }

            capturingAction = null;
            dirty = false;
        }

        private void OnGUI()
        {
            HandleCapture();

            EditorGUILayout.LabelField("输入映射", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "点某一行的按键按钮，然后按下想要的键（鼠标键也可以）。Esc 取消。\n"
                + "文档键位表里「鼠标左键/：平A/闪避」缺了一个字，当前取 平A = 鼠标左键、闪避 = 鼠标右键。",
                MessageType.Info);

            EditorGUILayout.Space();

            for (int i = 0; i < InputActions.Count; i++)
            {
                InputActionId action = InputActions.All[i];
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(action.ToDisplayName(), GUILayout.Width(150f));
                DrawSlot(action, 0, "主键");
                DrawSlot(action, 1, "备用");

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复文档默认"))
            {
                table.ResetToDefaults();
                dirty = true;
                status = "已恢复为文档默认键位，记得保存。";
            }

            using (new EditorGUI.DisabledScope(settings == null || !dirty))
            {
                if (GUILayout.Button("保存到 InputSettings"))
                {
                    Save();
                }
            }

            if (GUILayout.Button("重新读取"))
            {
                Reload();
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, dirty ? MessageType.Warning : MessageType.None);
            }

            DrawIssues();
        }

        private void DrawSlot(InputActionId action, int slot, string label)
        {
            InputKey key = table.GetKey(action, slot);
            bool capturing = capturingAction.HasValue && capturingAction.Value == action && capturingSlot == slot;
            string text = capturing ? "按下按键…" : (label + "：" + InputKeyCatalog.ToDisplayName(key));

            if (GUILayout.Button(text, GUILayout.Width(170f)))
            {
                capturingAction = action;
                capturingSlot = slot;
                status = "正在捕捉「" + action.ToDisplayName() + "」的" + (slot == 0 ? "主键" : "备用键") + "，Esc 取消。";
                Repaint();
            }
        }

        private void DrawIssues()
        {
            if (table == null)
            {
                return;
            }

            var report = new ValidationReport();
            table.CollectIssues(report);
            if (report.IsClean)
            {
                return;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < report.Issues.Count && i < 6; i++)
            {
                builder.AppendLine("· " + report.Issues[i].Message);
            }

            if (report.Issues.Count > 6)
            {
                builder.AppendLine("…… 其余 " + (report.Issues.Count - 6) + " 条见 Console");
            }

            EditorGUILayout.HelpBox(builder.ToString(), report.HasErrors ? MessageType.Error : MessageType.Warning);
        }

        private void HandleCapture()
        {
            if (!capturingAction.HasValue)
            {
                return;
            }

            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            if (current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.Escape)
                {
                    capturingAction = null;
                    status = "已取消捕捉。";
                    current.Use();
                    Repaint();
                    return;
                }

                InputKey key = InputKeyCatalog.FromKeyCode(current.keyCode);
                if (key != InputKey.None)
                {
                    Apply(key);
                    current.Use();
                }
            }
            else if (current.type == EventType.MouseDown)
            {
                InputKey key = current.button == 0
                    ? InputKey.MouseLeft
                    : current.button == 1 ? InputKey.MouseRight : InputKey.MouseMiddle;
                Apply(key);
                current.Use();
            }
        }

        private void Apply(InputKey key)
        {
            InputActionId action = capturingAction.Value;
            if (table.TrySetKey(action, capturingSlot, key, out InputActionId conflict))
            {
                dirty = true;
                status = "「" + action.ToDisplayName() + "」" + (capturingSlot == 0 ? "主键" : "备用键")
                    + " 已改为 " + InputKeyCatalog.ToDisplayName(key) + "，记得保存。";
            }
            else
            {
                status = "改不了：" + InputKeyCatalog.ToDisplayName(key)
                    + " 已经被「" + conflict.ToDisplayName() + "」占用。";
            }

            capturingAction = null;
            Repaint();
        }

        private void Save()
        {
            if (settings == null)
            {
                return;
            }

            settings.WriteTable(table);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            dirty = false;
            status = "已保存到 " + RingTools.InputSettingsPath;
        }
    }
}
