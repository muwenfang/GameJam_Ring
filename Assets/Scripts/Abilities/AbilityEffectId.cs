using System;
using System.Collections.Generic;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 一条 effectRef 的作用对象。让「同一个原语」能区分是打在自己身上还是命中目标身上，
    /// 对应 <see cref="AbilityDefinition"/> 里「Self 形状修饰施法者、其余形状修饰目标」的既有约定。
    /// </summary>
    public enum AbilityEffectScope
    {
        /// <summary>未指定，由执行器按技能形状推断。</summary>
        Auto = 0,

        /// <summary>施法者自身。</summary>
        Self = 1,

        /// <summary>被命中的单个目标。</summary>
        Target = 2,

        /// <summary>范围内的全部目标。</summary>
        Area = 3,
    }

    /// <summary>
    /// M3 的 13 类效果原语 ID —— 对应《钟声_架构分析_详版》§1.3 末尾的抽象结论与
    /// 《Docs/04》第六节「13 类效果原语 → 字段映射」表。
    ///
    /// 这是「effectRefs → 执行器」映射的**键**：技能定义（资产里可改）只引用这些稳定 ID，
    /// 具体行为由 <see cref="AbilityExecutorRegistry"/> 注册的执行器提供。
    /// 新增技能 = 在资产里组合这些 ID + 填字段，不需要写新的框架代码（M3 3.1 验收标准）。
    ///
    /// 枚举数值会写进 .asset 存档，**只能追加，不能重排**。
    /// </summary>
    public enum AbilityEffectId
    {
        None = 0,

        /// <summary>① 直线弹道：Shape=Line + RangeCells/AreaWidthCells/ProjectileSpeed。</summary>
        LineProjectile = 1,

        /// <summary>② 扇形多弹丸：Shape=Fan + SpreadDegrees/PelletCount/PelletDamage。</summary>
        FanPellets = 2,

        /// <summary>③ 折返弹丸：Returns + ProjectileSpeed + SecondaryDamage。</summary>
        ReturningProjectile = 3,

        /// <summary>④ 矩形/圆形即时判定：Shape=Rect/Circle + AreaLengthCells/AreaWidthCells/RadiusCells。</summary>
        InstantArea = 4,

        /// <summary>⑤ 突进/冲刺（含分段判定）：DashCells + DashSpeedCellsPerSecond。</summary>
        Dash = 5,

        /// <summary>⑥ 位移/强制拉回：Shape=Line + RootSeconds + Notes 里的锚点语义。</summary>
        ForcedDisplacement = 6,

        /// <summary>⑦ 击退：KnockbackCells。</summary>
        Knockback = 7,

        /// <summary>⑧ 眩晕/定身/减速：StunSeconds/RootSeconds/SlowPercent+SlowSeconds/StunResistanceSeconds。</summary>
        CrowdControl = 8,

        /// <summary>⑨ 增益减益与乘区：MoveSpeedMultiplier/AttackSpeedMultiplier/DefenseMultiplier/
        /// DamageTakenMultiplier/DamageTakenBonusPercent/DamageReductionBonus/Stack* 等。</summary>
        StatModifier = 9,

        /// <summary>⑩ 治疗与护盾：HealAmount/HealPerSecond/ShieldAmount/ShieldSeconds。</summary>
        HealShield = 10,

        /// <summary>⑪ 净化：Notes 标注 + 施放时清负面（战斗层实现）。</summary>
        Cleanse = 11,

        /// <summary>⑫ 姿态/状态切换：DurationSeconds + BasicAttackDamageOverride/AttackIntervalOverride/
        /// MoveSpeedScalesWithHealth/CanCancelEarly。</summary>
        Stance = 12,

        /// <summary>⑬ 地形生成与时间流速场：Shape=Ground/Circle + DurationSeconds + SlowPercent。</summary>
        TerrainTimeField = 13,
    }

    /// <summary>13 类原语的元数据工具（ID 合法性、中文名、代码名、消费字段清单）。</summary>
    public static class AbilityEffectIds
    {
        /// <summary>13 类原语，顺序同架构文档 §1.3。</summary>
        public static readonly AbilityEffectId[] All =
        {
            AbilityEffectId.LineProjectile,
            AbilityEffectId.FanPellets,
            AbilityEffectId.ReturningProjectile,
            AbilityEffectId.InstantArea,
            AbilityEffectId.Dash,
            AbilityEffectId.ForcedDisplacement,
            AbilityEffectId.Knockback,
            AbilityEffectId.CrowdControl,
            AbilityEffectId.StatModifier,
            AbilityEffectId.HealShield,
            AbilityEffectId.Cleanse,
            AbilityEffectId.Stance,
            AbilityEffectId.TerrainTimeField,
        };

        public const int Count = 13;

        public static bool IsDefined(AbilityEffectId id)
        {
            return id >= AbilityEffectId.LineProjectile && id <= AbilityEffectId.TerrainTimeField;
        }

        public static string ToDisplayName(AbilityEffectId id)
        {
            switch (id)
            {
                case AbilityEffectId.LineProjectile: return "直线弹道";
                case AbilityEffectId.FanPellets: return "扇形多弹丸";
                case AbilityEffectId.ReturningProjectile: return "折返弹丸";
                case AbilityEffectId.InstantArea: return "矩形/圆形即时判定";
                case AbilityEffectId.Dash: return "突进/冲刺";
                case AbilityEffectId.ForcedDisplacement: return "位移/强制拉回";
                case AbilityEffectId.Knockback: return "击退";
                case AbilityEffectId.CrowdControl: return "眩晕/定身/减速";
                case AbilityEffectId.StatModifier: return "增益减益与乘区";
                case AbilityEffectId.HealShield: return "治疗与护盾";
                case AbilityEffectId.Cleanse: return "净化";
                case AbilityEffectId.Stance: return "姿态/状态切换";
                case AbilityEffectId.TerrainTimeField: return "地形与时间流速场";
                default: return "未定义原语(" + (int)id + ")";
            }
        }

        public static string ToCodeName(AbilityEffectId id)
        {
            return id.ToString();
        }

        /// <summary>该原语消费 <see cref="AbilityDefinition"/> 上的哪些字段，用于校验与文档生成。</summary>
        public static string ToRequiredFields(AbilityEffectId id)
        {
            switch (id)
            {
                case AbilityEffectId.LineProjectile: return "Shape/RangeCells/AreaWidthCells/ProjectileSpeed/Damage";
                case AbilityEffectId.FanPellets: return "Shape/SpreadDegrees/PelletCount/PelletDamage/ProjectileSpeed";
                case AbilityEffectId.ReturningProjectile: return "Returns/ProjectileSpeed/SecondaryDamage";
                case AbilityEffectId.InstantArea: return "Shape/AreaLengthCells/AreaWidthCells/RadiusCells";
                case AbilityEffectId.Dash: return "DashCells/DashSpeedCellsPerSecond/AreaLengthCells";
                case AbilityEffectId.ForcedDisplacement: return "Shape/RootSeconds/Notes(锚点)";
                case AbilityEffectId.Knockback: return "KnockbackCells";
                case AbilityEffectId.CrowdControl: return "StunSeconds/RootSeconds/SlowPercent/SlowSeconds/StunResistanceSeconds";
                case AbilityEffectId.StatModifier: return "MoveSpeedMultiplier/AttackSpeedMultiplier/DefenseMultiplier/DamageTakenMultiplier/DamageTakenBonusPercent/DamageReductionBonus/Stack*";
                case AbilityEffectId.HealShield: return "HealAmount/HealPerSecond/ShieldAmount/ShieldSeconds";
                case AbilityEffectId.Cleanse: return "Notes(清除负面)";
                case AbilityEffectId.Stance: return "DurationSeconds/BasicAttackDamageOverride/AttackIntervalOverride/MoveSpeedScalesWithHealth/CanCancelEarly";
                case AbilityEffectId.TerrainTimeField: return "Shape/DurationSeconds/SlowPercent/MoveSpeedMultiplier/Notes";
                default: return string.Empty;
            }
        }

        public static string ToDescription(AbilityEffectId id)
        {
            return ToDisplayName(id) + "｜字段：" + ToRequiredFields(id);
        }
    }

    /// <summary>
    /// 一条效果引用：架构文档 §6 里 <c>SkillDefinition.effectRefs[]</c> 在本工程里的落地形态。
    /// 它是**纯数据**（与 <see cref="AbilityDefinition"/> 一样只用 public 字段 + [Serializable]，
    /// 不引用 UnityEngine），可在 Inspector 里增删改；执行器由 <see cref="AbilityExecutorRegistry"/>
    /// 按 <see cref="EffectId"/> 解析，因此「改效果组合」不需要改代码。
    /// </summary>
    [Serializable]
    public sealed class AbilityEffectRef
    {
        /// <summary>指向 13 类原语中的哪一类。</summary>
        public AbilityEffectId EffectId = AbilityEffectId.None;

        /// <summary>作用对象；Auto 表示由执行器按技能形状推断。</summary>
        public AbilityEffectScope Scope = AbilityEffectScope.Auto;

        /// <summary>附加参数（例如减速 0.3、层数 3）。0 表示「用技能定义上的同名字段」。</summary>
        public float Parameter;

        /// <summary>策划备注，例如「分段冲撞的 1~4 格档位见 Notes」。</summary>
        public string Note = string.Empty;

        public AbilityEffectRef()
        {
        }

        public AbilityEffectRef(AbilityEffectId id, AbilityEffectScope scope = AbilityEffectScope.Auto, float parameter = 0f, string note = "")
        {
            EffectId = id;
            Scope = scope;
            Parameter = parameter;
            Note = note ?? string.Empty;
        }

        public AbilityEffectRef Clone()
        {
            return new AbilityEffectRef(EffectId, Scope, Parameter, Note);
        }

        public string ToDisplayName()
        {
            return AbilityEffectIds.ToDisplayName(EffectId) + "（" + ScopeToText(Scope) + "）";
        }

        public static string ScopeToText(AbilityEffectScope scope)
        {
            switch (scope)
            {
                case AbilityEffectScope.Self: return "自身";
                case AbilityEffectScope.Target: return "目标";
                case AbilityEffectScope.Area: return "范围";
                default: return "自动";
            }
        }

        public void CollectIssues(ValidationReport report, string context)
        {
            if (report == null)
            {
                return;
            }

            if (!AbilityEffectIds.IsDefined(EffectId))
            {
                report.Error(context, "效果原语 ID " + (int)EffectId + " 未定义（effectRefs 只能引用 13 类原语）。");
                return;
            }

            if (!AbilityExecutorRegistry.IsRegistered(EffectId))
            {
                report.Error(context, "效果原语「" + AbilityEffectIds.ToDisplayName(EffectId)
                    + "」没有注册执行器，运行时无法解析。");
            }

            if (Parameter < 0f)
            {
                report.Warn(context, "原语「" + AbilityEffectIds.ToDisplayName(EffectId) + "」的附加参数为负。");
            }
        }
    }
}
