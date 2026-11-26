using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Ring.Characters;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 技能数值资产的数据体（纯 C#，可被 Unity 直接序列化）。
    ///
    /// 一份数据 = 30 条 <see cref="AbilityDefinition"/>（含各自的 effectRefs）
    ///          + 一份 <see cref="BalanceDecision"/>（Q1 决策与换算参数）。
    /// <see cref="AbilityTuningAsset"/> 把它包成 ScriptableObject 存成 .asset；
    /// 离线测试也可以直接 new 一个实例，注入 <see cref="AbilityCatalog"/> 验证「改表不改码」。
    /// </summary>
    [Serializable]
    public sealed class AbilityTuningData
    {
        /// <summary>结构版本，迁移用。当前为 1。</summary>
        public int SchemaVersion = 1;

        /// <summary>来源标签，写进日志与校验报告。</summary>
        public string SourceLabel = "AbilityTuning.asset";

        /// <summary>30 条能力数值；Inspector 里可逐条改。</summary>
        public List<AbilityDefinition> Definitions = new List<AbilityDefinition>();

        /// <summary>Q1 决策 + 换算公式参数。</summary>
        public BalanceDecision Balance = BalanceDecision.CreateDocumentDefault();

        /// <summary>从代码种子生成一份完整的资产数据（含自动推导的 effectRefs）。</summary>
        public static AbilityTuningData CreateFromSeed()
        {
            return new AbilityTuningData
            {
                SchemaVersion = 1,
                SourceLabel = "AbilityTuning.asset（从代码种子生成）",
                Definitions = AbilityCatalog.CreateSeedDefinitions(),
                Balance = BalanceDecision.CreateDocumentDefault(),
            };
        }

        /// <summary>转成可注入 <see cref="AbilityCatalog"/> 的数据源。</summary>
        public IAbilityCatalogSource ToSource()
        {
            return new AbilityDefinitionListSource(Definitions, SourceLabel);
        }

        public bool TryGet(PersonaId persona, AbilitySlot slot, out AbilityDefinition definition)
        {
            return ToSource().TryGet(persona, slot, out definition);
        }

        /// <summary>把数据注入门面 + 换算决策，使「改表」立即生效。</summary>
        public void Apply()
        {
            AbilityCatalog.SetSource(ToSource());
            if (Balance != null)
            {
                BalanceDecisionRuntime.Configure(Balance);
            }
        }

        /// <summary>为缺少 effectRefs 的条目按字段自动补全（Editor 菜单 / 迁移旧资产用）。</summary>
        public int EnsureEffectRefs()
        {
            int filled = 0;
            if (Definitions == null)
            {
                return filled;
            }

            for (int i = 0; i < Definitions.Count; i++)
            {
                AbilityDefinition definition = Definitions[i];
                if (definition == null)
                {
                    continue;
                }

                if (definition.EffectRefs == null || definition.EffectRefs.Count == 0)
                {
                    definition.EffectRefs = AbilityEffectRefs.InferFrom(definition);
                    filled++;
                }
            }

            return filled;
        }

        /// <summary>条目是否齐全（30 条且每对 (人设,槽位) 都有）。</summary>
        public bool IsComplete()
        {
            if (Definitions == null || Definitions.Count != AbilityCatalog.TotalCount)
            {
                return false;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < Definitions.Count; i++)
            {
                AbilityDefinition d = Definitions[i];
                if (d == null || !PersonaIds.IsDefined(d.Persona) || !d.Slot.IsDefined())
                {
                    return false;
                }

                if (!seen.Add((int)d.Persona + ":" + (int)d.Slot))
                {
                    return false;
                }
            }

            return seen.Count == AbilityCatalog.TotalCount;
        }

        /// <summary>完整自检：表结构 + 每条定义 + effectRefs 映射 + Q1 决策。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "技能数值资产";

            if (SchemaVersion < 1)
            {
                report.Warn(context, "结构版本小于 1，可能是旧格式。");
            }

            if (Definitions == null || Definitions.Count == 0)
            {
                report.Error(context, "Definitions 为空，注入后会导致全系统取不到技能数值。请用「从代码种子生成」补齐。");
            }
            else
            {
                ToSource().CollectIssues(report);
                int withRefs = 0;
                for (int i = 0; i < Definitions.Count; i++)
                {
                    if (Definitions[i] != null && Definitions[i].EffectRefs != null && Definitions[i].EffectRefs.Count > 0)
                    {
                        withRefs++;
                    }
                }

                report.Info(context, withRefs + "/" + Definitions.Count + " 条能力配置了 effectRefs。");
            }

            if (Balance == null)
            {
                report.Error(context, "Balance（Q1 决策）为空。");
            }
            else
            {
                Balance.CollectIssues(report);
            }

            AbilityExecutorRegistry.CollectIssues(report);
        }

        /// <summary>
        /// 与代码种子逐字段比对，列出被策划改过的条目。
        /// 用于 Editor 菜单「校验资产与代码种子差异」：能一眼看出改了哪些数值，而不是靠人肉比对。
        /// </summary>
        public IReadOnlyList<string> CompareWithSeed()
        {
            var diffs = new List<string>();
            IReadOnlyList<AbilityDefinition> seed = AbilityCatalog.SeedDefinitions;
            for (int i = 0; i < seed.Count; i++)
            {
                AbilityDefinition expected = seed[i];
                AbilityDefinition actual;
                if (!TryGet(expected.Persona, expected.Slot, out actual) || actual == null)
                {
                    diffs.Add(expected.ToDisplayName() + "：资产里缺失");
                    continue;
                }

                List<string> fields = CompareFields(expected, actual);
                if (fields.Count > 0)
                {
                    diffs.Add(expected.ToDisplayName() + "：" + string.Join("、", fields));
                }
            }

            return diffs;
        }

        private static List<string> CompareFields(AbilityDefinition a, AbilityDefinition b)
        {
            var result = new List<string>();
            FieldInfo[] fields = typeof(AbilityDefinition).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.FieldType == typeof(List<AbilityEffectRef>))
                {
                    continue; // effectRefs 单独看，不参与数值比对
                }

                object va = field.GetValue(a);
                object vb = field.GetValue(b);
                if (!ValuesEqual(field.FieldType, va, vb))
                {
                    result.Add(field.Name + " " + Format(va) + "→" + Format(vb));
                }
            }

            return result;
        }

        private static bool ValuesEqual(Type type, object a, object b)
        {
            if (type == typeof(float))
            {
                return Math.Abs((float)a - (float)b) <= 0.0005f;
            }

            if (type == typeof(string))
            {
                return string.Equals((string)a, (string)b, StringComparison.Ordinal);
            }

            return Equals(a, b);
        }

        private static string Format(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            if (value is float)
            {
                return ((float)value).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            }

            string text = value.ToString();
            return text.Length > 18 ? text.Substring(0, 18) + "…" : text;
        }

        public string ToOverview()
        {
            var builder = new StringBuilder();
            builder.Append("技能数值资产：").Append(Definitions == null ? 0 : Definitions.Count).Append(" 条")
                .Append("｜结构版本 ").Append(SchemaVersion)
                .Append("｜").Append(Balance != null ? Balance.ToSummary() : "Balance 为空");
            if (Definitions != null)
            {
                for (int i = 0; i < Definitions.Count; i++)
                {
                    AbilityDefinition d = Definitions[i];
                    if (d == null)
                    {
                        continue;
                    }

                    builder.Append('\n').Append(d.ToDisplayName())
                        .Append("｜").Append(d.ToSummary())
                        .Append("｜效果：").Append(d.ToEffectSummary());
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// 换算决策的运行时持有者。刻意放在纯 C# 层（不引用 UnityEngine），
    /// 让 <see cref="Ring.Characters.CharacterBalance"/> 与离线测试都能读到同一份配置。
    /// </summary>
    public static class BalanceDecisionRuntime
    {
        private static BalanceDecision active = BalanceDecision.CreateDocumentDefault();

        public static BalanceDecision Active => active;

        public static void Configure(BalanceDecision decision)
        {
            active = decision != null ? decision : BalanceDecision.CreateDocumentDefault();
        }

        public static void ResetToDefault()
        {
            active = BalanceDecision.CreateDocumentDefault();
        }
    }
}
