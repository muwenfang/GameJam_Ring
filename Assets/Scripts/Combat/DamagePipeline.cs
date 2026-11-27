using System;
using System.Collections.Generic;

namespace Ring.Combat
{
    /// <summary>伤害结算阶段。顺序由 <see cref="DamagePipelineSettings.StageOrder"/> 配置，验收要求顺序明确。</summary>
    public enum DamageStage
    {
        Defense = 0,
        Outgoing = 1,
        Incoming = 2,
        Shield = 3,
        Lethal = 4
    }

    /// <summary>一次伤害请求。Direction 用于击退与方向性减伤。</summary>
    public sealed class DamageRequest
    {
        public int AttackerId;
        public int TargetId;
        public float RawDamage;
        public int Tags;
        public Vec2 Direction;
        public Vec2 HitPoint;
        public float KnockbackCells;
        public float OutgoingMultiplier = 1f;
        public string Source = string.Empty;

        public DamageRequest Clone()
        {
            return new DamageRequest
            {
                AttackerId = AttackerId,
                TargetId = TargetId,
                RawDamage = RawDamage,
                Tags = Tags,
                Direction = Direction,
                HitPoint = HitPoint,
                KnockbackCells = KnockbackCells,
                OutgoingMultiplier = OutgoingMultiplier,
                Source = Source
            };
        }

        public override string ToString()
        {
            return "Damage(" + RawDamage.ToString("0.##") + ", " + DamageTags.Describe(Tags)
                   + ", 来源 " + AttackerId + ")";
        }
    }

    /// <summary>结算时目标的防御面数值；减伤取派生值，护盾由 CombatantState 提供。</summary>
    public sealed class DamageStats
    {
        public float Defense;
        public float DamageReduction;
        public float FlatReduction;
        public float OutgoingMultiplier = 1f;
        public float IncomingMultiplier = 1f;
        public float Shield;
        public bool Invulnerable;
        public bool IgnoreDefense;
        public string BlockReason = string.Empty;
    }

    public sealed class DamageResult
    {
        public bool Blocked;
        public bool LethalIntercepted;
        public bool Killed;
        public float HealthDamage;
        public float ShieldAbsorbed;
        public float RawDamage;
        public string BlockReason = string.Empty;
        public readonly List<DamageStage> Stages = new List<DamageStage>(6);
        public readonly List<string> Trace = new List<string>(6);

        public float TotalMitigated => RawDamage - HealthDamage;

        public override string ToString()
        {
            return Blocked
                ? "DamageResult(Blocked: " + BlockReason + ")"
                : "DamageResult(血量 " + HealthDamage.ToString("0.##") + ", 护盾 " + ShieldAbsorbed.ToString("0.##")
                  + (LethalIntercepted ? ", 致死被拦截" : string.Empty) + ")";
        }
    }

    public sealed class DamagePipelineSettings
    {
        /// <summary>默认顺序：基础伤害 → 防御/减伤 → 增伤 → 易伤 → 护盾 → 致死判定。</summary>
        public List<DamageStage> StageOrder = new List<DamageStage>
        {
            DamageStage.Defense,
            DamageStage.Outgoing,
            DamageStage.Incoming,
            DamageStage.Shield,
            DamageStage.Lethal
        };

        public float MinimumHealthDamage;
        public float DefenseToReductionDivisor = 500f;

        public DamagePipelineSettings Clone()
        {
            return new DamagePipelineSettings
            {
                StageOrder = new List<DamageStage>(StageOrder),
                MinimumHealthDamage = MinimumHealthDamage,
                DefenseToReductionDivisor = DefenseToReductionDivisor
            };
        }
    }

    /// <summary>
    /// M2.3 单点伤害结算。护盾先于血量；致死判定可插入，逆时通过 lethalInterceptor 接管。
    /// </summary>
    public sealed class DamagePipeline
    {
        private readonly DamagePipelineSettings settings;

        public DamagePipeline()
            : this(new DamagePipelineSettings())
        {
        }

        public DamagePipeline(DamagePipelineSettings settings)
        {
            this.settings = settings ?? new DamagePipelineSettings();
        }

        public DamagePipelineSettings Settings => settings;

        public static float ReductionFromDefense(float defense, float divisor)
        {
            if (defense <= 0f || divisor <= 0f)
            {
                return 0f;
            }

            return CombatMath.Clamp01(defense / divisor);
        }

        public DamageResult Resolve(DamageRequest request, DamageStats stats, float currentHealth, Func<bool> lethalInterceptor)
        {
            var result = new DamageResult
            {
                RawDamage = request != null ? Math.Max(0f, request.RawDamage) : 0f
            };

            if (request == null || result.RawDamage <= 0f)
            {
                result.Blocked = true;
                result.BlockReason = "无效伤害";
                return result;
            }

            stats ??= new DamageStats();
            if (stats.Invulnerable)
            {
                result.Blocked = true;
                result.BlockReason = string.IsNullOrEmpty(stats.BlockReason) ? "无敌" : stats.BlockReason;
                return result;
            }

            float damage = result.RawDamage;
            for (int i = 0; i < settings.StageOrder.Count; i++)
            {
                DamageStage stage = settings.StageOrder[i];
                if (result.Stages.Contains(stage))
                {
                    continue;
                }

                result.Stages.Add(stage);
                switch (stage)
                {
                    case DamageStage.Defense:
                        {
                            float reduction = stats.IgnoreDefense ? 0f : CombatMath.Clamp01(stats.DamageReduction);
                            if (reduction <= 0f && stats.Defense > 0f && !stats.IgnoreDefense)
                            {
                                reduction = ReductionFromDefense(stats.Defense, settings.DefenseToReductionDivisor);
                            }

                            float before = damage;
                            damage = ApplyReduction(damage, reduction);
                            damage = Math.Max(0f, damage - Math.Max(0f, stats.FlatReduction));
                            result.Trace.Add("防御减伤 " + before.ToString("0.##") + "→" + damage.ToString("0.##")
                                             + "（减伤 " + (reduction * 100f).ToString("0.#") + "%）");
                            break;
                        }

                    case DamageStage.Outgoing:
                        damage *= Math.Max(0f, stats.OutgoingMultiplier);
                        result.Trace.Add("输出乘区 ×" + stats.OutgoingMultiplier.ToString("0.###") + " → " + damage.ToString("0.##"));
                        break;

                    case DamageStage.Incoming:
                        damage *= Math.Max(0f, stats.IncomingMultiplier);
                        result.Trace.Add("受伤乘区 ×" + stats.IncomingMultiplier.ToString("0.###") + " → " + damage.ToString("0.##"));
                        break;

                    case DamageStage.Shield:
                        {
                            float shield = Math.Max(0f, stats.Shield);
                            float absorbed = Math.Min(shield, damage);
                            damage -= absorbed;
                            result.ShieldAbsorbed = absorbed;
                            if (absorbed > 0f)
                            {
                                result.Trace.Add("护盾吸收 " + absorbed.ToString("0.##") + " → " + damage.ToString("0.##"));
                            }

                            break;
                        }

                    case DamageStage.Lethal:
                        if (damage >= Math.Max(0f, currentHealth) && lethalInterceptor != null && lethalInterceptor())
                        {
                            result.LethalIntercepted = true;
                            result.Trace.Add("致死判定：逆时拦截，伤害归零");
                            damage = 0f;
                        }

                        break;
                }
            }

            damage = Math.Max(settings.MinimumHealthDamage, damage);
            result.HealthDamage = Math.Max(0f, damage);
            result.Killed = !result.LethalIntercepted && result.HealthDamage >= Math.Max(0f, currentHealth);
            if (!result.LethalIntercepted && result.HealthDamage <= 0f)
            {
                result.Blocked = true;
                result.BlockReason = "伤害被完全吸收";
            }

            return result;
        }

        public static float ApplyReduction(float rawDamage, float damageReduction)
        {
            return Math.Max(0f, rawDamage) * (1f - CombatMath.Clamp01(damageReduction));
        }
    }
}
