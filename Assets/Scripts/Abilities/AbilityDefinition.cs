using System;
using Ring.Characters;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>技能的作用形状。只用于数据描述与后续战斗层分工，不影响属性计算。</summary>
    public enum AbilityShape
    {
        /// <summary>未指定（纯被动/纯数值效果）。</summary>
        None = 0,

        /// <summary>作用于自身。</summary>
        Self = 1,

        /// <summary>矩形（长 × 宽）。</summary>
        Rect = 2,

        /// <summary>直线弹道（长 × 宽）。</summary>
        Line = 3,

        /// <summary>扇形（射程 + 总散射角）。</summary>
        Fan = 4,

        /// <summary>圆形（半径）。</summary>
        Circle = 5,

        /// <summary>地面区域（可在前方指定格）。</summary>
        Ground = 6,
    }

    /// <summary>
    /// 一条能力的完整数值定义。30 条（6 人设 × 5 槽）的默认值在 <see cref="AbilityCatalog"/>，
    /// 这里只负责存数据、自检与摘要，不含任何运行时状态。
    ///
    /// 数值约定（避免用 nullable 造成 YAML 难读）：
    ///   · 0 = 该字段不生效；
    ///   · 倍率类字段 0 表示「无修正」，填 1.5 就是 ×1.5；
    ///   · 百分比类字段填 0.3 表示 30%。
    /// 冷却为 0 表示「跟随身体的基础攻击间隔」（普攻槽就是这样）。
    ///
    /// 增益/减益的归属约定：
    ///   · <see cref="AbilityShape.Self"/> 的技能，治疗/减伤/倍率类字段是**自己**的增益；
    ///   · 其余形状（矩形/扇形/直线/圆形/地面）的这些字段修饰的是**命中目标**（例如催龄的
    ///     攻速/防御 −20%）；控制类字段（减速/眩晕/定身/击退）永远作用于目标。
    /// 这套约定让「一条数据同时描述施法者与受害者」变得可判定，后续战斗层照此执行即可。
    /// </summary>
    [Serializable]
    public sealed class AbilityDefinition
    {
        // ---------- 身份 ----------
        public PersonaId Persona;
        public AbilitySlot Slot;
        public string AbilityName = string.Empty;
        public string Description = string.Empty;

        // ---------- 作用方式 ----------
        public AbilityShape Shape;

        /// <summary>需要鼠标指定位置（缚地荆棘是唯一一条）。</summary>
        public bool RequiresPointerTarget;

        /// <summary>施法期间无法移动（紧急排气/自然恩赐这类短暂停顿）。</summary>
        public float ImmobileSeconds;

        /// <summary>施法期间有无敌帧（绝对防御不是无敌，是减伤）。</summary>
        public bool InvulnerableDuring;

        /// <summary>可再次点击提前结束（绝对防御/沉腰调息）。</summary>
        public bool CanCancelEarly;

        // ---------- 时间 ----------
        public float CooldownSeconds;
        public float WindupSeconds;

        /// <summary>姿态/状态/地形的持续时间。</summary>
        public float DurationSeconds;

        // ---------- 空间 ----------
        public float RangeCells;
        public float AreaLengthCells;
        public float AreaWidthCells;
        public float RadiusCells;
        public float SpreadDegrees;

        // ---------- 伤害 ----------
        public float Damage;
        public float PelletDamage;
        public int PelletCount;

        /// <summary>弹丸速度（格/秒）。0 表示近战即时判定。</summary>
        public float ProjectileSpeed;

        /// <summary>弹丸飞到最远端后折返（时钟飞刃）。</summary>
        public bool Returns;

        /// <summary>折返/二段伤害（时钟飞刃回程 4、狂战投矛 8）。</summary>
        public float SecondaryDamage;

        /// <summary>需要命中多少枚弹丸才触发附加效果（高压喷射 ≥3 枚减速）。</summary>
        public int HitsRequiredForBonus;

        // ---------- 位移 ----------
        public float DashCells;
        public float DashSpeedCellsPerSecond;

        // ---------- 控制 ----------
        public float SlowPercent;
        public float SlowSeconds;
        public float StunSeconds;
        public float RootSeconds;
        public float KnockbackCells;

        /// <summary>易伤：目标受到伤害 +15% 时填 0.15。</summary>
        public float DamageTakenBonusPercent;

        // ---------- 治疗与增益 ----------
        public float HealAmount;
        public float HealPerSecond;
        public float ShieldAmount;
        public float ShieldSeconds;

        /// <summary>减伤绝对值加成（敛息 +30% 减伤填 0.3）。</summary>
        public float DamageReductionBonus;

        /// <summary>受伤倍率（紧急排气 受伤 −25% 填 0.75）。</summary>
        public float DamageTakenMultiplier;

        /// <summary>移速倍率（高性能模式 ×1.5、绝对防御 ×0.5）。</summary>
        public float MoveSpeedMultiplier;

        /// <summary>移速随当前生命比例缩放（过载损耗：移速 = 基础 × 当前生命 / 生命上限）。</summary>
        public bool MoveSpeedScalesWithHealth;

        /// <summary>攻击速度倍率（高性能模式 ×1.5）。</summary>
        public float AttackSpeedMultiplier;

        /// <summary>防御倍率（狂战 防御 −30% 填 0.7）。</summary>
        public float DefenseMultiplier;

        /// <summary>防御绝对值加成（斯巴达韧性 +8）。</summary>
        public float DefenseFlatBonus;

        /// <summary>直接覆盖攻击间隔（狂战单刀 0.5 秒）。</summary>
        public float AttackIntervalOverride;

        /// <summary>覆盖平A伤害（狂战单刀 2 点）。</summary>
        public float BasicAttackDamageOverride;

        // ---------- 层数 / 计数 ----------
        public int MaxStacks;
        public float StackDurationSeconds;

        /// <summary>每层增伤（剑意 0.1 = +10%）。</summary>
        public float StackDamageBonus;

        /// <summary>每层移速加成（风之优雅 0.05 = +5%）。</summary>
        public float StackMoveSpeedBonus;

        /// <summary>满层时下一次攻击的额外伤害（重箭 +2）。</summary>
        public float StackAttackDamageBonus;

        /// <summary>需要累计多少次才触发（力量 3 次平A / 过载损耗 3 次受击）。</summary>
        public int RequiredHitsForTrigger;

        /// <summary>计数窗口：多久没再次命中就清零（力量的 1.5 秒）。</summary>
        public float TriggerWindowSeconds;

        /// <summary>触发后给目标的抗性时长（眩晕后 1.5 秒免疫）。</summary>
        public float StunResistanceSeconds;

        // ---------- 备注 ----------
        public string Notes = string.Empty;

        /// <summary>是否是常驻被动。</summary>
        public bool IsPassive
        {
            get { return Slot.IsPassive(); }
        }

        /// <summary>是否是普攻槽。</summary>
        public bool IsBasicAttack
        {
            get { return Slot.IsBasicAttack(); }
        }

        /// <summary>是否有任意主动冷却（普攻为 false）。</summary>
        public bool HasCooldown
        {
            get { return CooldownSeconds > 0f; }
        }

        public AbilityDefinition Clone()
        {
            var copy = new AbilityDefinition();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(AbilityDefinition other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            Persona = other.Persona;
            Slot = other.Slot;
            AbilityName = other.AbilityName;
            Description = other.Description;
            Shape = other.Shape;
            RequiresPointerTarget = other.RequiresPointerTarget;
            ImmobileSeconds = other.ImmobileSeconds;
            InvulnerableDuring = other.InvulnerableDuring;
            CanCancelEarly = other.CanCancelEarly;
            CooldownSeconds = other.CooldownSeconds;
            WindupSeconds = other.WindupSeconds;
            DurationSeconds = other.DurationSeconds;
            RangeCells = other.RangeCells;
            AreaLengthCells = other.AreaLengthCells;
            AreaWidthCells = other.AreaWidthCells;
            RadiusCells = other.RadiusCells;
            SpreadDegrees = other.SpreadDegrees;
            Damage = other.Damage;
            PelletDamage = other.PelletDamage;
            PelletCount = other.PelletCount;
            ProjectileSpeed = other.ProjectileSpeed;
            Returns = other.Returns;
            SecondaryDamage = other.SecondaryDamage;
            HitsRequiredForBonus = other.HitsRequiredForBonus;
            DashCells = other.DashCells;
            DashSpeedCellsPerSecond = other.DashSpeedCellsPerSecond;
            SlowPercent = other.SlowPercent;
            SlowSeconds = other.SlowSeconds;
            StunSeconds = other.StunSeconds;
            RootSeconds = other.RootSeconds;
            KnockbackCells = other.KnockbackCells;
            DamageTakenBonusPercent = other.DamageTakenBonusPercent;
            HealAmount = other.HealAmount;
            HealPerSecond = other.HealPerSecond;
            ShieldAmount = other.ShieldAmount;
            ShieldSeconds = other.ShieldSeconds;
            DamageReductionBonus = other.DamageReductionBonus;
            DamageTakenMultiplier = other.DamageTakenMultiplier;
            MoveSpeedMultiplier = other.MoveSpeedMultiplier;
            MoveSpeedScalesWithHealth = other.MoveSpeedScalesWithHealth;
            AttackSpeedMultiplier = other.AttackSpeedMultiplier;
            DefenseMultiplier = other.DefenseMultiplier;
            DefenseFlatBonus = other.DefenseFlatBonus;
            AttackIntervalOverride = other.AttackIntervalOverride;
            BasicAttackDamageOverride = other.BasicAttackDamageOverride;
            MaxStacks = other.MaxStacks;
            StackDurationSeconds = other.StackDurationSeconds;
            StackDamageBonus = other.StackDamageBonus;
            StackMoveSpeedBonus = other.StackMoveSpeedBonus;
            StackAttackDamageBonus = other.StackAttackDamageBonus;
            RequiredHitsForTrigger = other.RequiredHitsForTrigger;
            TriggerWindowSeconds = other.TriggerWindowSeconds;
            StunResistanceSeconds = other.StunResistanceSeconds;
            Notes = other.Notes;
        }

        /// <summary>技能卡上显示的名字：「剑客·本我 突进斩」。</summary>
        public string ToDisplayName()
        {
            string persona = PersonaIds.ToChineseName(Persona);
            return persona + "·" + Slot.ToDisplayName() + (string.IsNullOrEmpty(AbilityName) ? string.Empty : " " + AbilityName);
        }

        /// <summary>一行摘要，Editor 列表与日志用。</summary>
        public string ToSummary()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(ToDisplayName());

            if (Damage > 0f)
            {
                builder.Append("｜伤害 ").Append(AbilityMath.Number(Damage));
            }

            if (PelletCount > 0 && PelletDamage > 0f)
            {
                builder.Append("｜").Append(PelletCount).Append("×").Append(AbilityMath.Number(PelletDamage));
            }

            if (CooldownSeconds > 0f)
            {
                builder.Append("｜CD ").Append(AbilityMath.Number(CooldownSeconds)).Append("s");
            }

            if (WindupSeconds > 0f)
            {
                builder.Append("｜前摇 ").Append(AbilityMath.Number(WindupSeconds)).Append("s");
            }

            if (RangeCells > 0f)
            {
                builder.Append("｜射程 ").Append(AbilityMath.Number(RangeCells)).Append(" 格");
            }

            if (RadiusCells > 0f)
            {
                builder.Append("｜半径 ").Append(AbilityMath.Number(RadiusCells)).Append(" 格");
            }

            if (AreaLengthCells > 0f && AreaWidthCells > 0f)
            {
                builder.Append("｜").Append(AbilityMath.Number(AreaLengthCells)).Append("×")
                    .Append(AbilityMath.Number(AreaWidthCells));
            }

            if (MaxStacks > 0)
            {
                builder.Append("｜").Append(MaxStacks).Append(" 层");
            }

            return builder.ToString();
        }

        /// <summary>数据自检：只查「明显不可能是策划意图」的配置错误与遗漏。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            string context = ToDisplayName();

            if (!PersonaIds.IsDefined(Persona))
            {
                report.Error(context, "人设标识未定义。");
            }

            if (!Slot.IsDefined())
            {
                report.Error(context, "槽位未定义。");
            }

            if (string.IsNullOrWhiteSpace(AbilityName))
            {
                report.Warn(context, "没有填技能名。");
            }

            if (CooldownSeconds < 0f)
            {
                report.Error(context, "冷却不能为负。");
            }

            if (WindupSeconds < 0f)
            {
                report.Error(context, "前摇不能为负。");
            }

            if (WindupSeconds > 0f && CooldownSeconds > 0f && WindupSeconds >= CooldownSeconds)
            {
                report.Warn(context, "前摇长于等于冷却，技能永远无法转好。");
            }

            if (RangeCells < 0f || AreaLengthCells < 0f || AreaWidthCells < 0f || RadiusCells < 0f)
            {
                report.Error(context, "射程/范围不能为负。");
            }

            if (PelletCount > 0 && PelletDamage <= 0f)
            {
                report.Warn(context, "有多发弹丸但没有弹丸伤害。");
            }

            if (PelletCount <= 0 && PelletDamage > 0f)
            {
                report.Warn(context, "填了弹丸伤害但弹丸数量为 0。");
            }

            if (Returns && ProjectileSpeed <= 0f)
            {
                report.Warn(context, "折返弹丸没有弹速。");
            }

            if (MaxStacks > 0 && StackDurationSeconds <= 0f)
            {
                report.Warn(context, "层数上限 > 0 但层数持续时间为 0，层会立刻消失。");
            }

            if (MaxStacks > 0 && StackDamageBonus <= 0f && StackMoveSpeedBonus <= 0f && StackAttackDamageBonus <= 0f)
            {
                report.Warn(context, "有层数但没有层收益。");
            }

            if (RequiredHitsForTrigger > 0 && StunSeconds <= 0f && KnockbackCells <= 0f && SlowPercent <= 0f
                && DamageTakenBonusPercent <= 0f && MaxStacks <= 0)
            {
                report.Warn(context, "填了触发次数但没有触发效果。");
            }

            if (StunSeconds > 0f && StunResistanceSeconds <= 0f)
            {
                report.Info(context, "有眩晕但没有眩晕抗性时长（文档里力士的眩晕带 1.5s 抗性）。");
            }

            if (Shape == AbilityShape.Fan && SpreadDegrees <= 0f)
            {
                report.Warn(context, "扇形技能没有散射角。");
            }

            if (Shape == AbilityShape.Circle && RadiusCells <= 0f)
            {
                report.Warn(context, "圆形技能没有半径。");
            }

            if (Shape == AbilityShape.Rect && (AreaLengthCells <= 0f || AreaWidthCells <= 0f))
            {
                report.Warn(context, "矩形技能缺少长或宽。");
            }

            if (HealPerSecond > 0f && DurationSeconds <= 0f && Slot.IsSkill())
            {
                report.Info(context, "持续回血但持续时间为 0，会按一次性处理。");
            }

            if (MoveSpeedMultiplier > 0f && AbilityMath.Approximately(MoveSpeedMultiplier, 1f))
            {
                report.Info(context, "移速倍率填了 1（等于没有修正），可以留 0。");
            }
        }
    }
}
