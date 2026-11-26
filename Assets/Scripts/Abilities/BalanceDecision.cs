using System;
using System.Collections.Generic;
using Ring.Characters;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 减伤/移速的换算策略。这是架构文档 §9-Q1 的**决策开关**：
    /// 策划改变主意时只改资产里的这一个枚举，不需要改任何 C# 代码。
    /// </summary>
    public enum ReductionPolicy
    {
        /// <summary>一律按公式，忽略所有覆盖值（对应「统一为 ÷5，游侠改成 15%」）。</summary>
        FormulaOnly = 0,

        /// <summary>默认：公式 + 显式特例覆盖表（对应「保留文档 12.5%」）。</summary>
        DocumentExceptions = 1,

        /// <summary>只认覆盖表；未覆盖的人设减伤按 0 处理，要求覆盖表覆盖全部 6 人。</summary>
        OverrideOnly = 2,
    }

    /// <summary>
    /// 一条「某人设的减伤覆盖值」，附审计信息。
    /// 每条覆盖都必须写清依据与后续动作，避免又变成一句没有出处的硬编码。
    /// </summary>
    [Serializable]
    public sealed class PersonaReductionOverride
    {
        public PersonaId Persona = PersonaId.Ranger;

        /// <summary>覆盖后的减伤（0~1）。</summary>
        public float DamageReduction = 0.125f;

        /// <summary>为什么覆盖：引用文档原文或策划结论。</summary>
        public string Reason = string.Empty;

        /// <summary>出处（文件名 + 章节/行）。</summary>
        public string Source = string.Empty;

        /// <summary>决议人；未与策划确认时写「待确认」。</summary>
        public string DecidedBy = "待确认";

        /// <summary>决议日期（YYYY-MM-DD）。</summary>
        public string DecidedOn = string.Empty;

        /// <summary>后续动作：确认后要同步改哪些地方。</summary>
        public string FollowUp = string.Empty;

        public PersonaReductionOverride Clone()
        {
            return new PersonaReductionOverride
            {
                Persona = Persona,
                DamageReduction = DamageReduction,
                Reason = Reason,
                Source = Source,
                DecidedBy = DecidedBy,
                DecidedOn = DecidedOn,
                FollowUp = FollowUp,
            };
        }
    }

    /// <summary>
    /// Q1（减伤公式与游侠特例）的完整决策记录 + 可配置换算参数。
    ///
    /// 过去 Q1 只落地成 <c>CharacterBalance.HasDocumentedException</c> 里一句
    /// <c>return persona == Ranger</c>：既没有记录「问题是什么、有哪些选项、谁定的、
    /// 改主意怎么办」，公式除数也是 const 硬编码。本类把这些全部抬到资产层：
    ///   · 公式除数 / 移速系数可在 Inspector 改；
    ///   · 三种 <see cref="ReductionPolicy"/> 一键切换；
    ///   · 覆盖表带依据、出处、决议人、日期、后续动作；
    ///   · <see cref="ResolveReduction"/> 是唯一计算入口，校验与回填都走它。
    /// </summary>
    [Serializable]
    public sealed class BalanceDecision
    {
        /// <summary>减伤 = 防御 / 本除数。500 对应「防御 ÷ 5 = 百分数」。</summary>
        public float DefenseToReductionDivisor = 500f;

        /// <summary>移速(格/秒) = 速度 × 本系数。</summary>
        public float SpeedToCellsPerSecond = 0.03f;

        /// <summary>浮点比较容差。</summary>
        public float Tolerance = 0.0005f;

        public ReductionPolicy Policy = ReductionPolicy.DocumentExceptions;

        public List<PersonaReductionOverride> Overrides = new List<PersonaReductionOverride>();

        // ---------- Q1 决策记录（审计用，不参与计算） ----------

        public string Q1Status = "待策划确认（工程侧已按「保留文档值」实现）";

        public string Q1Question = "防御 70/82/90/96/120 均满足「减伤 = 防御÷500」（百分数 = 防御÷5）；"
            + "唯独精灵游侠防御 75 在总表里写 12.5%，按公式应为 15%（相当于 ÷600）。"
            + "架构分析 §9-Q1 建议统一为 ÷5 并确认游侠值。";

        public string Q1Options = "A 统一按公式 15%（改写文档总表）；"
            + "B 保留文档 12.5% 并登记为显式特例；"
            + "C 改公式除数使六行全部自洽（不存在能同时满足 6 行的单一除数，已排除）。";

        public string Q1Decision = "取 B：保留 12.5%。依据是文档总表用 ⚠️ 显式标注该值，"
            + "视为策划定稿，工程不得擅自改写设计数值；同时把「这是特例」写进资产而不是代码判断。";

        public string Q1DecidedBy = "工程侧取舍（待策划最终确认）";

        public string Q1DecidedOn = "2026-11-26";

        public string Q1FollowUp = "若策划确认改按 15%：把本资产 Policy 切为 FormulaOnly（或删掉 Ranger 覆盖），"
            + "再用 Editor 菜单「按公式回填（含文档特例）」同步六份 Character_*.asset，"
            + "并更新 Docs/01 的总表与 CharacterReferenceTable 快照。全程不需要改 C# 代码。";

        public string Notes = string.Empty;

        // ---------- 计算 ----------

        /// <summary>按当前策略解析某个人设的最终减伤（0~1）。这是唯一计算入口。</summary>
        public float ResolveReduction(PersonaId persona, float defense)
        {
            float formula = ReductionFromDefense(defense);
            if (Policy == ReductionPolicy.FormulaOnly)
            {
                return formula;
            }

            PersonaReductionOverride found;
            if (TryGetOverride(persona, out found))
            {
                return Clamp01(found.DamageReduction);
            }

            return Policy == ReductionPolicy.OverrideOnly ? 0f : formula;
        }

        /// <summary>纯公式换算（不看覆盖表）。</summary>
        public float ReductionFromDefense(float defense)
        {
            if (defense <= 0f || DefenseToReductionDivisor <= 0f)
            {
                return 0f;
            }

            return Clamp01(defense / DefenseToReductionDivisor);
        }

        public float ResolveMoveSpeed(float speed)
        {
            return Math.Max(0f, speed) * Math.Max(0f, SpeedToCellsPerSecond);
        }

        public bool HasOverride(PersonaId persona)
        {
            PersonaReductionOverride ignored;
            return TryGetOverride(persona, out ignored);
        }

        public bool TryGetOverride(PersonaId persona, out PersonaReductionOverride result)
        {
            if (Overrides != null)
            {
                for (int i = 0; i < Overrides.Count; i++)
                {
                    if (Overrides[i] != null && Overrides[i].Persona == persona)
                    {
                        result = Overrides[i];
                        return true;
                    }
                }
            }

            result = null;
            return false;
        }

        public bool Approximately(float a, float b)
        {
            return Math.Abs(a - b) <= Math.Max(0f, Tolerance);
        }

        public static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        // ---------- 默认与拷贝 ----------

        /// <summary>文档定稿口径的默认决策：公式 + 游侠 12.5% 显式特例。</summary>
        public static BalanceDecision CreateDocumentDefault()
        {
            var decision = new BalanceDecision
            {
                Policy = ReductionPolicy.DocumentExceptions,
                Overrides = new List<PersonaReductionOverride>
                {
                    new PersonaReductionOverride
                    {
                        Persona = PersonaId.Ranger,
                        DamageReduction = 0.125f,
                        Reason = "文档总表把精灵游侠的减伤写为 12.5%，并用 ⚠️ 标注与公式 15% 的偏差；按「不擅自改写设计数值」原则保留。",
                        Source = "GameDesign/钟声_架构分析_详版.md §1.2 总表、§9-Q1；Docs/01_角色数值与输入系统.md §一.3",
                        DecidedBy = "工程侧取舍（待策划最终确认）",
                        DecidedOn = "2026-11-26",
                        FollowUp = "策划确认后：若取 15%，切 Policy=FormulaOnly 或删除本条覆盖；若维持 12.5%，把 DecidedBy 改为策划并补决议日期。",
                    },
                },
            };

            return decision;
        }

        private static readonly BalanceDecision DocumentDefaultCache = CreateDocumentDefault();

        /// <summary>代码种子 / 离线回退用的只读默认决策。</summary>
        public static BalanceDecision DocumentDefault => DocumentDefaultCache;

        public BalanceDecision Clone()
        {
            var copy = new BalanceDecision
            {
                DefenseToReductionDivisor = DefenseToReductionDivisor,
                SpeedToCellsPerSecond = SpeedToCellsPerSecond,
                Tolerance = Tolerance,
                Policy = Policy,
                Q1Status = Q1Status,
                Q1Question = Q1Question,
                Q1Options = Q1Options,
                Q1Decision = Q1Decision,
                Q1DecidedBy = Q1DecidedBy,
                Q1DecidedOn = Q1DecidedOn,
                Q1FollowUp = Q1FollowUp,
                Notes = Notes,
                Overrides = new List<PersonaReductionOverride>(),
            };

            if (Overrides != null)
            {
                for (int i = 0; i < Overrides.Count; i++)
                {
                    copy.Overrides.Add(Overrides[i] != null ? Overrides[i].Clone() : null);
                }
            }

            return copy;
        }

        // ---------- 自检 ----------

        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "数值换算决策(Q1)";

            if (DefenseToReductionDivisor <= 0f)
            {
                report.Error(context, "减伤公式除数必须大于 0。");
            }

            if (SpeedToCellsPerSecond <= 0f)
            {
                report.Error(context, "移速换算系数必须大于 0。");
            }

            if (Tolerance < 0f)
            {
                report.Warn(context, "容差为负，比较会永远不成立。");
            }

            if (string.IsNullOrWhiteSpace(Q1Decision))
            {
                report.Warn(context, "Q1 没有填写决议内容。");
            }

            if (string.IsNullOrWhiteSpace(Q1Status))
            {
                report.Info(context, "Q1 没有标注确认状态。");
            }

            var seen = new HashSet<PersonaId>();
            if (Overrides == null)
            {
                report.Warn(context, "覆盖表为 null。");
            }
            else
            {
                for (int i = 0; i < Overrides.Count; i++)
                {
                    PersonaReductionOverride item = Overrides[i];
                    if (item == null)
                    {
                        report.Error(context, "第 " + i + " 条覆盖为空。");
                        continue;
                    }

                    if (!PersonaIds.IsDefined(item.Persona))
                    {
                        report.Error(context, "第 " + i + " 条覆盖的人设 ID 未定义。");
                    }

                    if (!seen.Add(item.Persona))
                    {
                        report.Error(context, PersonaIds.ToChineseName(item.Persona) + " 有重复的减伤覆盖。");
                    }

                    if (item.DamageReduction < 0f || item.DamageReduction > 1f)
                    {
                        report.Error(context, PersonaIds.ToChineseName(item.Persona) + " 的覆盖减伤必须在 0~1 之间。");
                    }

                    if (string.IsNullOrWhiteSpace(item.Reason) || string.IsNullOrWhiteSpace(item.Source))
                    {
                        report.Warn(context, PersonaIds.ToChineseName(item.Persona) + " 的覆盖缺少依据或出处，容易再次变成没来由的硬编码。");
                    }
                }
            }

            if (Policy == ReductionPolicy.OverrideOnly)
            {
                int covered = Overrides == null ? 0 : seen.Count;
                if (covered < PersonaIds.Count)
                {
                    report.Warn(context, "OverrideOnly 策略下只覆盖了 " + covered + "/" + PersonaIds.Count
                        + " 个人设，其余人设减伤会按 0 处理。");
                }
            }

            PersonaReductionOverride ranger;
            if (TryGetOverride(PersonaId.Ranger, out ranger))
            {
                float formula = ReductionFromDefense(75f);
                if (!Approximately(ranger.DamageReduction, formula))
                {
                    report.Info(context, "游侠减伤 " + AbilityMath.Percent(ranger.DamageReduction)
                        + " 与公式 " + AbilityMath.Percent(formula) + " 不一致 —— 这是 Q1 登记的文档特例，"
                        + "当前策略 " + Policy + " 下按覆盖值生效。");
                }
            }
        }

        public string ToSummary()
        {
            return "Policy=" + Policy + "｜减伤=防御/" + AbilityMath.Number(DefenseToReductionDivisor)
                + "｜移速=速度×" + AbilityMath.Number(SpeedToCellsPerSecond)
                + "｜覆盖 " + (Overrides == null ? 0 : Overrides.Count) + " 人"
                + "｜Q1 状态：" + Q1Status;
        }
    }
}
