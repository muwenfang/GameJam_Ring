using System;
using System.Collections.Generic;
using System.Text;
using Ring.Core;

namespace Ring.Inputs
{
    /// <summary>
    /// 按键映射的文本序列化。
    ///
    /// 选文本而不是二进制（JsonUtility 也行，但要引 UnityEngine）的原因：
    /// 1. 纯 C#，可以离线跑测试；
    /// 2. 存的是枚举「名字」而不是数值，将来往 <see cref="InputKey"/> 末尾加键、
    ///    甚至调整顺序，旧存档都不会静默错位；
    /// 3. 出问题时可以直接看 PlayerPrefs 里的内容。
    ///
    /// 格式：
    /// <code>
    /// # ring.input.bindings v1
    /// MoveUp=W|UpArrow
    /// Attack=MouseLeft|None
    /// </code>
    /// </summary>
    public static class InputBindingSerializer
    {
        /// <summary>PlayerPrefs 里保存映射的键名。</summary>
        public const string PlayerPrefsKey = "ring.input.bindings.v1";

        /// <summary>当前格式版本。</summary>
        public const int Version = 1;

        private const string Header = "# ring.input.bindings v1";

        /// <summary>序列化成文本。</summary>
        public static string Serialize(InputBindingTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            var builder = new StringBuilder();
            builder.Append(Header);

            IReadOnlyList<InputActionId> all = InputActions.All;
            for (int i = 0; i < all.Count; i++)
            {
                InputActionId action = all[i];
                builder.Append('\n')
                    .Append(action.ToString())
                    .Append('=')
                    .Append(table.GetPrimary(action).ToString())
                    .Append('|')
                    .Append(table.GetAlternate(action).ToString());
            }

            return builder.ToString();
        }

        /// <summary>
        /// 解析文本。缺少的动作会退回文档默认键位，因此旧存档/手改坏一行不会让游戏没法操作。
        /// </summary>
        public static bool TryDeserialize(string text, out InputBindingTable table, out string error)
        {
            table = new InputBindingTable();
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "映射文本为空。";
                return false;
            }

            var problems = new List<string>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int applied = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    problems.Add("第 " + (i + 1) + " 行格式不对：\"" + line + "\"");
                    continue;
                }

                string actionName = line.Substring(0, separator).Trim();
                string keysPart = line.Substring(separator + 1).Trim();

                if (!Enum.TryParse(actionName, true, out InputActionId action)
                    || !InputActions.IsDefined(action))
                {
                    problems.Add("未知动作 \"" + actionName + "\"");
                    continue;
                }

                string[] keys = keysPart.Split('|');
                InputKey primary = ParseKey(keys, 0, actionName, problems);
                InputKey alternate = ParseKey(keys, 1, actionName, problems);
                table.Set(action, primary, alternate);
                applied++;
            }

            if (applied == 0)
            {
                error = problems.Count > 0 ? string.Join("；", problems) : "没有解析到任何绑定。";
                return false;
            }

            error = problems.Count > 0 ? string.Join("；", problems) : null;
            return true;
        }

        /// <summary>校验文本是否可解析，同时报告缺省了哪些动作。</summary>
        public static bool Validate(string text, out IReadOnlyList<string> problems)
        {
            var list = new List<string>();
            bool ok = TryDeserialize(text, out InputBindingTable table, out string error);
            if (!string.IsNullOrEmpty(error))
            {
                list.Add(error);
            }

            if (ok)
            {
                var report = new ValidationReport();
                table.CollectIssues(report);
                for (int i = 0; i < report.Issues.Count; i++)
                {
                    list.Add(report.Issues[i].ToString());
                }
            }

            problems = list;
            return ok && list.Count == 0;
        }

        private static InputKey ParseKey(string[] parts, int index, string actionName, List<string> problems)
        {
            if (parts == null || index >= parts.Length)
            {
                return InputKey.None;
            }

            string raw = parts[index].Trim();
            if (raw.Length == 0)
            {
                return InputKey.None;
            }

            if (!Enum.TryParse(raw, true, out InputKey key))
            {
                problems.Add(actionName + " 的第 " + (index + 1) + " 个键 \"" + raw + "\" 不认识");
                return InputKey.None;
            }

            return key;
        }
    }
}
