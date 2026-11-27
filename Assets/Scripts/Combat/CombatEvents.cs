using System;
using Ring.Abilities;

namespace Ring.Combat
{
    /// <summary>伤害标签，用位标记而不是 enum，方便组合与过滤。</summary>
    public static class DamageTags
    {
        public const int None = 0;
        public const int BasicAttack = 1 << 0;
        public const int Skill = 1 << 1;
        public const int Projectile = 1 << 2;
        public const int DamageOverTime = 1 << 3;
        public const int Environment = 1 << 4;
        public const int TrueDamage = 1 << 5;

        public static bool Has(int tags, int flag) => (tags & flag) != 0;

        public static string Describe(int tags)
        {
            if (tags == None)
            {
                return "无";
            }

            var parts = new System.Collections.Generic.List<string>(4);
            if (Has(tags, BasicAttack)) parts.Add("平A");
            if (Has(tags, Skill)) parts.Add("技能");
            if (Has(tags, Projectile)) parts.Add("弹道");
            if (Has(tags, DamageOverTime)) parts.Add("持续伤害");
            if (Has(tags, Environment)) parts.Add("环境");
            if (Has(tags, TrueDamage)) parts.Add("真实伤害");
            return string.Join("+", parts);
        }
    }

    public enum StateRestoreMode
    {
        PhysiologicalOnly = 0,
        Full = 1
    }

    public readonly struct DamageEvent
    {
        public readonly int AttackerId;
        public readonly int TargetId;
        public readonly float Amount;
        public readonly float Absorbed;
        public readonly int Tags;
        public readonly bool Lethal;
        public readonly bool Blocked;

        public DamageEvent(int attackerId, int targetId, float amount, float absorbed, int tags, bool lethal, bool blocked)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Amount = amount;
            Absorbed = absorbed;
            Tags = tags;
            Lethal = lethal;
            Blocked = blocked;
        }

        public override string ToString()
        {
            return "OnDamaged(" + AttackerId + "→" + TargetId + ", " + Amount.ToString("0.##")
                   + ", 吸收 " + Absorbed.ToString("0.##") + ", " + DamageTags.Describe(Tags)
                   + (Blocked ? ", 被免疫" : string.Empty) + ")";
        }
    }

    public readonly struct DeathEvent
    {
        public readonly int VictimId;
        public readonly int KillerId;

        public DeathEvent(int victimId, int killerId)
        {
            VictimId = victimId;
            KillerId = killerId;
        }

        public override string ToString() => "OnDied(" + VictimId + ", 击杀者 " + KillerId + ")";
    }

    public readonly struct HealEvent
    {
        public readonly int SourceId;
        public readonly int TargetId;
        public readonly float Amount;

        public HealEvent(int sourceId, int targetId, float amount)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Amount = amount;
        }

        public override string ToString() => "OnHealed(" + TargetId + ", +" + Amount.ToString("0.##") + ")";
    }

    public readonly struct ShieldEvent
    {
        public readonly int TargetId;
        public readonly float Current;
        public readonly float Delta;

        public ShieldEvent(int targetId, float current, float delta)
        {
            TargetId = targetId;
            Current = current;
            Delta = delta;
        }

        public override string ToString() => "OnShieldChanged(" + TargetId + ", " + Current.ToString("0.##") + ")";
    }

    public readonly struct BuffChangedEvent
    {
        public readonly int TargetId;
        public readonly string BuffId;
        public readonly bool Added;
        public readonly int Stacks;

        public BuffChangedEvent(int targetId, string buffId, bool added, int stacks)
        {
            TargetId = targetId;
            BuffId = buffId;
            Added = added;
            Stacks = stacks;
        }

        public override string ToString() => "OnBuffChanged(" + TargetId + ", " + BuffId + ", " + (Added ? "获得" : "失去") + ", " + Stacks + "层)";
    }

    public readonly struct SkillCastEvent
    {
        public readonly int CasterId;
        public readonly AbilitySlot Slot;
        public readonly string AbilityName;
        public readonly Vec2 AimPoint;
        public readonly bool HasAim;

        public SkillCastEvent(int casterId, AbilitySlot slot, string abilityName, Vec2 aimPoint, bool hasAim)
        {
            CasterId = casterId;
            Slot = slot;
            AbilityName = abilityName;
            AimPoint = aimPoint;
            HasAim = hasAim;
        }

        public override string ToString() => "OnSkillCast(" + CasterId + ", " + Slot + ", " + AbilityName + ")";
    }

    public readonly struct StateRestoredEvent
    {
        public readonly int TargetId;
        public readonly StateRestoreMode Mode;
        public readonly float SecondsAgo;
        public readonly float Health;

        public StateRestoredEvent(int targetId, StateRestoreMode mode, float secondsAgo, float health)
        {
            TargetId = targetId;
            Mode = mode;
            SecondsAgo = secondsAgo;
            Health = health;
        }

        public override string ToString() => "OnStateRestored(" + TargetId + ", " + Mode + ", " + SecondsAgo.ToString("0.##") + "s)";
    }

    /// <summary>
    /// M2.6 事件总线。战斗核心只向这里发事件，M4/M7/M8/M9 只订阅，不反向持有战斗对象。
    /// 静态事件在退出 Play 或重开局时由 <see cref="ClearAll"/> 清空，避免回调指向上局已被销毁的对象。
    /// </summary>
    public static class CombatEvents
    {
        public static event Action<DamageEvent> OnDamaged;
        public static event Action<DeathEvent> OnDied;
        public static event Action<HealEvent> OnHealed;
        public static event Action<ShieldEvent> OnShieldChanged;
        public static event Action<BuffChangedEvent> OnBuffChanged;
        public static event Action<SkillCastEvent> OnSkillCast;
        public static event Action<StateRestoredEvent> OnStateRestored;

        public static void RaiseDamaged(DamageEvent evt) => OnDamaged?.Invoke(evt);
        public static void RaiseDied(DeathEvent evt) => OnDied?.Invoke(evt);
        public static void RaiseHealed(HealEvent evt) => OnHealed?.Invoke(evt);
        public static void RaiseShieldChanged(ShieldEvent evt) => OnShieldChanged?.Invoke(evt);
        public static void RaiseBuffChanged(BuffChangedEvent evt) => OnBuffChanged?.Invoke(evt);
        public static void RaiseSkillCast(SkillCastEvent evt) => OnSkillCast?.Invoke(evt);
        public static void RaiseStateRestored(StateRestoredEvent evt) => OnStateRestored?.Invoke(evt);

        public static void ClearAll()
        {
            OnDamaged = null;
            OnDied = null;
            OnHealed = null;
            OnShieldChanged = null;
            OnBuffChanged = null;
            OnSkillCast = null;
            OnStateRestored = null;
        }
    }
}
