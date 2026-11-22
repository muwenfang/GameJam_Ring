using System.Collections.Generic;
using Ring.Core;
using UnityEngine;

namespace Ring.Inputs
{
    /// <summary>
    /// 输入映射的设计期配置：默认键位 + 是否允许玩家改键 + 是否写本地存储。
    /// 文档要求「自定义：映射表 + 本地存储」，这里把「默认映射表」放在资产里，
    /// 玩家改键后的结果则在运行时由 <see cref="GameInput"/> 落到 PlayerPrefs。
    /// </summary>
    [CreateAssetMenu(fileName = "InputSettings", menuName = "钟声/输入映射", order = 10)]
    public sealed class InputSettings : ScriptableObject
    {
        [Tooltip("默认键位。留空则完全取代码里的文档默认值。")]
        [SerializeField] private List<InputBindingEntry> defaultBindings = new List<InputBindingEntry>();

        [Tooltip("是否允许玩家在运行时改键。")]
        [SerializeField] private bool allowRuntimeRebinding = true;

        [Tooltip("是否把玩家改键结果写进 PlayerPrefs（键名见 InputBindingSerializer.PlayerPrefsKey）。")]
        [SerializeField] private bool persistToPlayerPrefs = true;

        [TextArea(2, 5)]
        [Tooltip("键位决策与存疑点，留给策划看。")]
        [SerializeField] private string designNotes = string.Empty;

        /// <summary>设计期默认键位（可能为空）。</summary>
        public IReadOnlyList<InputBindingEntry> DefaultBindings => defaultBindings;

        /// <summary>是否允许运行时改键。</summary>
        public bool AllowRuntimeRebinding => allowRuntimeRebinding;

        /// <summary>是否持久化玩家改键。</summary>
        public bool PersistToPlayerPrefs => persistToPlayerPrefs;

        /// <summary>键位备注。</summary>
        public string DesignNotes => designNotes;

        /// <summary>
        /// 生成一份映射表：先铺文档默认值，再用资产里的条目覆盖。
        /// 这样即使资产少配了几行，也不会让某个动作变成「没有键」。
        /// </summary>
        public InputBindingTable CreateTable()
        {
            var table = new InputBindingTable();
            table.ApplyEntries(defaultBindings);
            return table;
        }

        /// <summary>把一份映射表写回资产（编辑器工具用）。</summary>
        public void WriteTable(InputBindingTable table)
        {
            if (table == null)
            {
                return;
            }

            defaultBindings.Clear();
            defaultBindings.AddRange(table.ToEntries());
        }

        /// <summary>恢复为代码里的文档默认键位。</summary>
        public void ResetToDocumentDefaults()
        {
            WriteTable(new InputBindingTable());
        }

        /// <summary>收集本资产的问题。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "输入映射资产";

            if (defaultBindings == null || defaultBindings.Count == 0)
            {
                report.Info(context, "没有配置默认绑定，将直接使用代码里的文档默认键位。");
                return;
            }

            var seen = new HashSet<InputActionId>();
            for (int i = 0; i < defaultBindings.Count; i++)
            {
                InputBindingEntry entry = defaultBindings[i];
                if (!InputActions.IsDefined(entry.Action))
                {
                    report.Error(context, "第 " + (i + 1) + " 条的动作为未定义值 " + (int)entry.Action + "。");
                    continue;
                }

                if (!seen.Add(entry.Action))
                {
                    report.Error(context, entry.Action.ToDisplayName() + " 出现了多次，只有最后一条生效。");
                }
            }

            CreateTable().CollectIssues(report);
        }
    }
}
