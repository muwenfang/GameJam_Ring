using System;

namespace Ring.Combat
{
    /// <summary>姿态配置：举盾 / 狂战 / 调息，以及狂战结束后的重新装备硬直。</summary>
    public sealed class CombatStanceSpec
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;
        public float Duration;
        public float MoveSpeedMultiplier = 1f;
        public float DamageTakenMultiplier = 1f;
        public bool FrontalOnly;
        public bool DisableBasicAttack;
        public bool DisableAllSkills;
        public bool DisableIdSkill;
        public bool KnockbackImmune;
        public bool Immobile;

        /// <summary>
        /// 姿态期间把平A 射程压到指定格数（0 = 不覆盖）。
        /// 现有 AbilityRuntime 的 AttackRangeOverride 只取 Math.Max，只能加长不能缩短，
        /// 而「单刀狂战」要求把长矛的 2 格压成 1 格，所以覆盖值放在姿态表。
        /// </summary>
        public float BasicAttackRangeOverride;

        public string EndLagStanceId = string.Empty;
        public float EndLagDuration;
        public bool ShieldOnPerfectEnd;
        public float PerfectShieldAmount;
        public float PerfectShieldSeconds;

        public CombatStanceSpec Clone()
        {
            return (CombatStanceSpec)MemberwiseClone();
        }

        public CombatStanceSpec WithDuration(float duration)
        {
            CombatStanceSpec copy = Clone();
            if (duration > 0f)
            {
                copy.Duration = duration;
            }

            return copy;
        }
    }

    /// <summary>代码内姿态表。数据驱动的技能只引用 id，避免把表现字段塞进 AbilityDefinition。</summary>
    public static class CombatStances
    {
        public const string ShieldWall = "spartan.shield_wall";
        public const string Berserk = "spartan.berserk";
        public const string BerserkRecovery = "spartan.berserk_recovery";
        public const string Meditation = "strongman.meditation";

        private static readonly CombatStanceSpec ShieldWallSpec = new CombatStanceSpec
        {
            Id = ShieldWall,
            DisplayName = "绝对防御",
            Duration = 2f,
            MoveSpeedMultiplier = 0.5f,
            DamageTakenMultiplier = 0.1f,
            FrontalOnly = true,
            DisableBasicAttack = true,
            DisableIdSkill = true
        };

        private static readonly CombatStanceSpec BerserkSpec = new CombatStanceSpec
        {
            Id = Berserk,
            DisplayName = "单刀狂战",
            Duration = 3f,
            // 平A 改单刀：射程 1 格、单发 2 伤害、间隔 0.5s。
            // 伤害与间隔由技能定义的 BasicAttackDamageOverride / AttackIntervalOverride 走修正器，
            // 射程无法用修正器下调，改由姿态表覆盖。
            BasicAttackRangeOverride = 1f,
            EndLagStanceId = BerserkRecovery,
            EndLagDuration = 1.5f
        };

        private static readonly CombatStanceSpec BerserkRecoverySpec = new CombatStanceSpec
        {
            Id = BerserkRecovery,
            DisplayName = "重新装备",
            Duration = 1.5f,
            MoveSpeedMultiplier = 0.7f,
            DisableBasicAttack = true,
            DisableAllSkills = true
        };

        private static readonly CombatStanceSpec MeditationSpec = new CombatStanceSpec
        {
            Id = Meditation,
            DisplayName = "沉腰调息",
            Duration = 2f,
            MoveSpeedMultiplier = 0.5f,
            DamageTakenMultiplier = 0.75f,
            DisableBasicAttack = true,
            DisableAllSkills = true,
            KnockbackImmune = true,
            ShieldOnPerfectEnd = true,
            PerfectShieldAmount = 8f,
            PerfectShieldSeconds = 3f
        };

        public static CombatStanceSpec Get(string id)
        {
            switch (id)
            {
                case ShieldWall:
                    return ShieldWallSpec.Clone();
                case Berserk:
                    return BerserkSpec.Clone();
                case BerserkRecovery:
                    return BerserkRecoverySpec.Clone();
                case Meditation:
                    return MeditationSpec.Clone();
                default:
                    return null;
            }
        }

        public static bool IsDefined(string id) => Get(id) != null;
    }

    public readonly struct StanceSnapshot
    {
        public readonly string Id;
        public readonly float Remaining;
        public readonly bool WasHit;

        public StanceSnapshot(string id, float remaining, bool wasHit)
        {
            Id = id;
            Remaining = remaining;
            WasHit = wasHit;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id) && Remaining > 0f;
    }

    /// <summary>M2.4 姿态运行时。姿态结束时按配置自动进入硬直姿态。</summary>
    public sealed class CombatStanceRuntime
    {
        private CombatStanceSpec current;
        private float remaining;
        private bool wasHit;
        private bool inEndLag;

        public CombatStanceSpec Current => current;
        public string CurrentId => current != null ? current.Id : string.Empty;
        public bool IsActive => current != null && remaining > 0f;
        public float Remaining => remaining;
        public bool WasHit => wasHit;
        public bool InEndLag => inEndLag;

        public bool DisableBasicAttack => IsActive && current.DisableBasicAttack;
        public bool DisableAllSkills => IsActive && current.DisableAllSkills;
        public bool DisableIdSkill => IsActive && current.DisableIdSkill;
        public bool KnockbackImmune => IsActive && current.KnockbackImmune;
        public bool Immobile => IsActive && current.Immobile;
        public float MoveSpeedMultiplier => IsActive ? Math.Max(0f, current.MoveSpeedMultiplier) : 1f;
        public float DamageTakenMultiplier => IsActive ? Math.Max(0f, current.DamageTakenMultiplier) : 1f;

        /// <summary>本帧刚刚自然结束的姿态（用于完美调息护盾等结算）。</summary>
        public CombatStanceSpec EndedThisTick { get; private set; }

        public bool EndedWithoutBeingHit { get; private set; }

        public event Action<CombatStanceSpec> Ended;

        public void Begin(CombatStanceSpec spec, float durationOverride = 0f, float endLagDurationOverride = 0f)
        {
            if (spec == null)
            {
                return;
            }

            current = spec.WithDuration(durationOverride);
            if (endLagDurationOverride > 0f)
            {
                current.EndLagDuration = endLagDurationOverride;
            }

            remaining = Math.Max(0.01f, current.Duration);
            wasHit = false;
            inEndLag = false;
            EndedThisTick = null;
            EndedWithoutBeingHit = false;
        }

        public void Cancel()
        {
            current = null;
            remaining = 0f;
            wasHit = false;
            inEndLag = false;
        }

        public void NotifyHit()
        {
            if (IsActive)
            {
                wasHit = true;
            }
        }

        public void Tick(float deltaTime)
        {
            EndedThisTick = null;
            EndedWithoutBeingHit = false;
            if (!IsActive || deltaTime <= 0f)
            {
                return;
            }

            remaining -= deltaTime;
            if (remaining > 0f)
            {
                return;
            }

            CombatStanceSpec finished = current;
            bool finishedWithoutHit = !wasHit;
            EndedThisTick = finished;
            EndedWithoutBeingHit = finishedWithoutHit;
            Ended?.Invoke(finished);

            string endLagId = finished.EndLagStanceId;
            if (!string.IsNullOrEmpty(endLagId) && !inEndLag)
            {
                CombatStanceSpec endLag = CombatStances.Get(endLagId);
                if (endLag != null)
                {
                    float duration = finished.EndLagDuration > 0f ? finished.EndLagDuration : endLag.Duration;
                    current = endLag.WithDuration(duration);
                    remaining = Math.Max(0.01f, current.Duration);
                    wasHit = false;
                    inEndLag = true;
                    return;
                }
            }

            current = null;
            remaining = 0f;
            wasHit = false;
            inEndLag = false;
        }

        public StanceSnapshot Capture()
        {
            return IsActive
                ? new StanceSnapshot(CurrentId, remaining, wasHit)
                : default;
        }

        public void Restore(StanceSnapshot snapshot)
        {
            Cancel();
            if (!snapshot.IsValid)
            {
                return;
            }

            CombatStanceSpec spec = CombatStances.Get(snapshot.Id);
            if (spec == null)
            {
                return;
            }

            current = spec.WithDuration(snapshot.Remaining);
            remaining = snapshot.Remaining;
            wasHit = snapshot.WasHit;
            inEndLag = snapshot.Id == CombatStances.BerserkRecovery;
        }
    }
}
