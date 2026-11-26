using System;
using System.Collections.Generic;
using Ring.Characters;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 技能数值的数据源。这是「改表不改码」的关键抽象：
    /// 全系统取数值只经过 <see cref="AbilityCatalog"/> 这一个门面，
    /// 而门面背后可以是「代码种子」也可以是「Inspector 里的 .asset 资产」。
    ///
    /// 接口本身是纯 C#，离线测试可以注入一份自定义表来验证「改表立即生效」。
    /// </summary>
    public interface IAbilityCatalogSource
    {
        /// <summary>来源名，用于日志与校验报告（例如「代码种子」/「AbilityTuning.asset」）。</summary>
        string SourceName { get; }

        /// <summary>全部定义（顺序为「人设 → 槽位」）。</summary>
        IReadOnlyList<AbilityDefinition> All { get; }

        /// <summary>按 (人设, 槽位) 取定义。</summary>
        bool TryGet(PersonaId persona, AbilitySlot slot, out AbilityDefinition definition);

        /// <summary>自检本数据源（数量、缺失、重复、每条定义、effectRefs 映射）。</summary>
        void CollectIssues(ValidationReport report);
    }

    /// <summary>
    /// 由一份 <see cref="AbilityDefinition"/> 列表构成的数据源。
    /// <see cref="AbilityTuningAsset"/> 把资产里的 30 条定义转成这个类型后注入门面；
    /// 离线测试也可以直接用它构造一张「改过数值的表」。
    /// </summary>
    public sealed class AbilityDefinitionListSource : IAbilityCatalogSource
    {
        private readonly AbilityDefinition[] items;
        private readonly Dictionary<int, AbilityDefinition> index;
        private readonly string sourceName;

        public AbilityDefinitionListSource(IEnumerable<AbilityDefinition> definitions, string sourceName = "数值表")
        {
            this.sourceName = string.IsNullOrEmpty(sourceName) ? "数值表" : sourceName;
            var buffer = new List<AbilityDefinition>();
            if (definitions != null)
            {
                foreach (AbilityDefinition definition in definitions)
                {
                    if (definition != null)
                    {
                        buffer.Add(definition);
                    }
                }
            }

            items = buffer.ToArray();
            index = new Dictionary<int, AbilityDefinition>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                AbilityDefinition definition = items[i];
                index[Key(definition.Persona, definition.Slot)] = definition;
            }
        }

        public string SourceName => sourceName;

        public IReadOnlyList<AbilityDefinition> All => items;

        public int Count => items.Length;

        public bool TryGet(PersonaId persona, AbilitySlot slot, out AbilityDefinition definition)
        {
            return index.TryGetValue(Key(persona, slot), out definition);
        }

        /// <summary>接口实现：按 30 条的标准口径自检。</summary>
        public void CollectIssues(ValidationReport report)
        {
            CollectIssues(report, AbilityCatalog.TotalCount);
        }

        /// <summary>自检：数量、缺失、重复、每条定义、以及 effectRefs 的执行器映射。</summary>
        public void CollectIssues(ValidationReport report, int expectedCount)
        {
            if (report == null)
            {
                return;
            }

            string context = "技能数值表[" + sourceName + "]";

            if (expectedCount > 0 && items.Length != expectedCount)
            {
                report.Error(context, "应有 " + expectedCount + " 条能力，实际 " + items.Length + " 条。");
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < items.Length; i++)
            {
                AbilityDefinition definition = items[i];
                string key = (int)definition.Persona + ":" + (int)definition.Slot;
                if (!seen.Add(key))
                {
                    report.Error(context, definition.ToDisplayName() + " 定义重复。");
                }

                definition.CollectIssues(report);
            }

            for (int p = 0; p < PersonaIds.Count; p++)
            {
                for (int s = 0; s < AbilitySlots.Count; s++)
                {
                    AbilityDefinition ignored;
                    if (!TryGet((PersonaId)p, AbilitySlots.All[s], out ignored))
                    {
                        report.Error(context, PersonaIds.ToChineseName((PersonaId)p) + " 缺少「"
                            + AbilitySlots.All[s].ToDisplayName() + "」能力。");
                    }
                }
            }
        }

        private static int Key(PersonaId persona, AbilitySlot slot)
        {
            return (int)persona * AbilitySlots.Count + (int)slot;
        }
    }
}
