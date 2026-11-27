using System;
using System.Collections.Generic;

namespace Ring.Combat
{
    public enum StatusKind
    {
        Slow = 0,
        Root = 1,
        Stun = 2,
        Vulnerability = 3,
        DamageReduction = 4,
        DamageDealt = 5,
        MoveSpeed = 6,
        AttackSpeed = 7,
        DefenseBuff = 8,
        DefenseShred = 9,
        DamageOverTime = 10
    }

    /// <summary>一条状态/增益的配置模板。运行时会复制一份，避免 ScriptableObject 被改。</summary>
    public sealed class CombatStatusSpec
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;
        public StatusKind Kind;
        public float Value;
        public float Duration;
        public int Stacks = 1;
        public int MaxStacks = 99;
        public int SourceId;
        public bool IsDebuff;
        public float TickInterval;
        public float TickDamage;

        public CombatStatusSpec Clone()
        {
            return new CombatStatusSpec
            {
                Id = Id,
                DisplayName = DisplayName,
                Kind = Kind,
                Value = Value,
                Duration = Duration,
                Stacks = Stacks,
                MaxStacks = MaxStacks,
                SourceId = SourceId,
                IsDebuff = IsDebuff,
                TickInterval = TickInterval,
                TickDamage = TickDamage
            };
        }

        public static CombatStatusSpec Slow(float percent, float seconds, int sourceId = 0)
        {
            return Debuff("slow", "减速", StatusKind.Slow, CombatMath.Clamp01(percent), seconds, sourceId);
        }

        public static CombatStatusSpec Stun(float seconds, int sourceId = 0)
        {
            return Debuff("stun", "眩晕", StatusKind.Stun, 1f, seconds, sourceId);
        }

        public static CombatStatusSpec Root(float seconds, int sourceId = 0)
        {
            return Debuff("root", "定身", StatusKind.Root, 1f, seconds, sourceId);
        }

        public static CombatStatusSpec Vulnerability(float percent, float seconds, int sourceId = 0)
        {
            return Debuff("vulnerability", "易伤", StatusKind.Vulnerability, Math.Max(0f, percent), seconds, sourceId);
        }

        public static CombatStatusSpec DamageOverTime(string id, float tickDamage, float tickInterval, float seconds, int sourceId = 0)
        {
            CombatStatusSpec spec = Debuff(string.IsNullOrEmpty(id) ? "dot" : id, "持续伤害", StatusKind.DamageOverTime, 0f, seconds, sourceId);
            spec.TickDamage = Math.Max(0f, tickDamage);
            spec.TickInterval = Math.Max(0.05f, tickInterval);
            return spec;
        }

        public static CombatStatusSpec Buff(string id, string displayName, StatusKind kind, float value, float seconds, int sourceId = 0)
        {
            return new CombatStatusSpec
            {
                Id = id,
                DisplayName = displayName,
                Kind = kind,
                Value = value,
                Duration = Math.Max(0f, seconds),
                SourceId = sourceId,
                IsDebuff = false
            };
        }

        public static CombatStatusSpec Debuff(string id, string displayName, StatusKind kind, float value, float seconds, int sourceId = 0)
        {
            return new CombatStatusSpec
            {
                Id = id,
                DisplayName = displayName,
                Kind = kind,
                Value = value,
                Duration = Math.Max(0f, seconds),
                SourceId = sourceId,
                IsDebuff = true
            };
        }

        public override string ToString()
        {
            return (string.IsNullOrEmpty(DisplayName) ? Id : DisplayName) + "(" + Kind + ", " + Value.ToString("0.##") + ")";
        }
    }

    public sealed class CombatStatusInstance
    {
        public CombatStatusSpec Spec;
        public float Remaining;
        public int Stacks = 1;
        public float TickTimer;

        public bool Expired => Remaining <= 0f;
        public string Id => Spec != null ? Spec.Id : string.Empty;
        public StatusKind Kind => Spec != null ? Spec.Kind : StatusKind.Slow;
        public bool IsDebuff => Spec != null && Spec.IsDebuff;

        public override string ToString()
        {
            return (Spec != null ? Spec.DisplayName : "?") + " " + Remaining.ToString("0.##") + "s ×" + Stacks;
        }
    }

    public readonly struct StatusSnapshot
    {
        public readonly string Id;
        public readonly StatusKind Kind;
        public readonly float Value;
        public readonly float Remaining;
        public readonly int Stacks;
        public readonly int SourceId;
        public readonly float TickInterval;
        public readonly float TickDamage;
        public readonly bool IsDebuff;
        public readonly int MaxStacks;
        public readonly string DisplayName;

        public StatusSnapshot(CombatStatusSpec spec, float remaining, int stacks)
        {
            Id = spec.Id;
            Kind = spec.Kind;
            Value = spec.Value;
            Remaining = remaining;
            Stacks = stacks;
            SourceId = spec.SourceId;
            TickInterval = spec.TickInterval;
            TickDamage = spec.TickDamage;
            IsDebuff = spec.IsDebuff;
            MaxStacks = spec.MaxStacks;
            DisplayName = spec.DisplayName;
        }

        public CombatStatusSpec ToSpec()
        {
            return new CombatStatusSpec
            {
                Id = Id,
                DisplayName = DisplayName,
                Kind = Kind,
                Value = Value,
                Duration = Remaining,
                Stacks = Stacks,
                MaxStacks = MaxStacks,
                SourceId = SourceId,
                IsDebuff = IsDebuff,
                TickInterval = TickInterval,
                TickDamage = TickDamage
            };
        }
    }

    public readonly struct StatusApplyResult
    {
        public readonly bool Added;
        public readonly bool Changed;
        public readonly int Stacks;
        public readonly CombatStatusInstance Instance;

        public StatusApplyResult(bool added, bool changed, int stacks, CombatStatusInstance instance)
        {
            Added = added;
            Changed = changed;
            Stacks = stacks;
            Instance = instance;
        }
    }

    /// <summary>
    /// M2.4 状态容器：增减益、控制（眩晕/定身/减速）、持续伤害、净化。
    /// 纯 C#，不引用 Unity，便于离线测试。
    /// </summary>
    public sealed class StatusRuntime
    {
        private readonly List<CombatStatusInstance> statuses = new List<CombatStatusInstance>(12);

        public event Action<CombatStatusInstance, bool> Changed;

        public IReadOnlyList<CombatStatusInstance> All => statuses;

        public int Count => statuses.Count;

        public bool IsStunned => HasKind(StatusKind.Stun);
        public bool IsRooted => HasKind(StatusKind.Root);
        public bool IsImmobile => IsStunned || IsRooted;

        public float StunRemaining => RemainingOf(StatusKind.Stun);
        public float RootRemaining => RemainingOf(StatusKind.Root);

        public float MoveSpeedMultiplier
        {
            get
            {
                float multiplier = 1f;
                for (int i = 0; i < statuses.Count; i++)
                {
                    CombatStatusInstance status = statuses[i];
                    if (status.Kind == StatusKind.Slow)
                    {
                        multiplier *= Math.Max(0f, 1f - status.Spec.Value);
                    }
                    else if (status.Kind == StatusKind.MoveSpeed)
                    {
                        multiplier *= Math.Max(0f, 1f + status.Spec.Value);
                    }
                }

                return CombatMath.Clamp(multiplier, 0.05f, 5f);
            }
        }

        public float AttackSpeedMultiplier
        {
            get
            {
                float multiplier = 1f;
                for (int i = 0; i < statuses.Count; i++)
                {
                    if (statuses[i].Kind == StatusKind.AttackSpeed)
                    {
                        multiplier *= Math.Max(0f, 1f + statuses[i].Spec.Value);
                    }
                }

                return CombatMath.Clamp(multiplier, 0.05f, 5f);
            }
        }

        public float DamageDealtMultiplier
        {
            get
            {
                float multiplier = 1f;
                for (int i = 0; i < statuses.Count; i++)
                {
                    if (statuses[i].Kind == StatusKind.DamageDealt)
                    {
                        multiplier *= Math.Max(0f, 1f + statuses[i].Spec.Value);
                    }
                }

                return Math.Max(0f, multiplier);
            }
        }

        public float DamageTakenMultiplier
        {
            get
            {
                float multiplier = 1f;
                for (int i = 0; i < statuses.Count; i++)
                {
                    CombatStatusInstance status = statuses[i];
                    if (status.Kind == StatusKind.Vulnerability)
                    {
                        multiplier *= Math.Max(0f, 1f + status.Spec.Value);
                    }
                    else if (status.Kind == StatusKind.DamageReduction)
                    {
                        multiplier *= Math.Max(0f, 1f - status.Spec.Value);
                    }
                }

                return CombatMath.Clamp(multiplier, 0.05f, 5f);
            }
        }

        public float DefenseMultiplier
        {
            get
            {
                float multiplier = 1f;
                for (int i = 0; i < statuses.Count; i++)
                {
                    CombatStatusInstance status = statuses[i];
                    if (status.Kind == StatusKind.DefenseBuff)
                    {
                        multiplier *= Math.Max(0f, 1f + status.Spec.Value);
                    }
                    else if (status.Kind == StatusKind.DefenseShred)
                    {
                        multiplier *= Math.Max(0f, 1f - status.Spec.Value);
                    }
                }

                return CombatMath.Clamp(multiplier, 0.05f, 5f);
            }
        }

        public bool TryGet(string id, out CombatStatusInstance status)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (string.Equals(statuses[i].Id, id, StringComparison.Ordinal))
                {
                    status = statuses[i];
                    return true;
                }
            }

            status = null;
            return false;
        }

        public StatusApplyResult Apply(CombatStatusSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Id) || spec.Duration <= 0f)
            {
                return default;
            }

            CombatStatusInstance existing;
            if (TryGet(spec.Id, out existing))
            {
                int beforeStacks = existing.Stacks;
                existing.Remaining = Math.Max(existing.Remaining, spec.Duration);
                existing.Stacks = Math.Min(Math.Max(1, spec.MaxStacks), existing.Stacks + Math.Max(1, spec.Stacks));
                if (spec.TickDamage > 0f)
                {
                    existing.Spec.TickDamage = Math.Max(existing.Spec.TickDamage, spec.TickDamage);
                    existing.Spec.TickInterval = spec.TickInterval > 0f ? spec.TickInterval : existing.Spec.TickInterval;
                }

                Changed?.Invoke(existing, false);
                return new StatusApplyResult(false, existing.Stacks != beforeStacks, existing.Stacks, existing);
            }

            var instance = new CombatStatusInstance
            {
                Spec = spec.Clone(),
                Remaining = spec.Duration,
                Stacks = Math.Max(1, Math.Min(Math.Max(1, spec.MaxStacks), spec.Stacks)),
                TickTimer = 0f
            };
            instance.Spec.Stacks = instance.Stacks;
            statuses.Add(instance);
            Changed?.Invoke(instance, true);
            return new StatusApplyResult(true, true, instance.Stacks, instance);
        }

        public bool Remove(string id)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (!string.Equals(statuses[i].Id, id, StringComparison.Ordinal))
                {
                    continue;
                }

                CombatStatusInstance removed = statuses[i];
                statuses.RemoveAt(i);
                Changed?.Invoke(removed, false);
                return true;
            }

            return false;
        }

        /// <summary>净化：移除最多 max 条负面状态；max&lt;=0 表示全部移除。返回移除数量。</summary>
        public int CleanseDebuffs(int max = 0)
        {
            int removed = 0;
            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                if (!statuses[i].IsDebuff)
                {
                    continue;
                }

                if (max > 0 && removed >= max)
                {
                    break;
                }

                CombatStatusInstance status = statuses[i];
                statuses.RemoveAt(i);
                removed++;
                Changed?.Invoke(status, false);
            }

            return removed;
        }

        public void Clear()
        {
            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                CombatStatusInstance status = statuses[i];
                statuses.RemoveAt(i);
                Changed?.Invoke(status, false);
            }
        }

        /// <summary>推进时间；持续伤害按 TickInterval 回调 onTick(伤害, 来源 id)。</summary>
        public void Tick(float deltaTime, Action<float, int> onTick)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                CombatStatusInstance status = statuses[i];
                status.Remaining -= deltaTime;

                if (status.Kind == StatusKind.DamageOverTime && status.Spec.TickDamage > 0f)
                {
                    status.TickTimer += deltaTime;
                    float interval = status.Spec.TickInterval > 0f ? status.Spec.TickInterval : 1f;
                    int guard = 0;
                    while (status.TickTimer >= interval && guard++ < 32)
                    {
                        status.TickTimer -= interval;
                        onTick?.Invoke(status.Spec.TickDamage * Math.Max(1, status.Stacks), status.Spec.SourceId);
                    }
                }

                if (status.Remaining > 0f)
                {
                    continue;
                }

                statuses.RemoveAt(i);
                Changed?.Invoke(status, false);
            }
        }

        public StatusSnapshot[] Capture()
        {
            var snapshots = new StatusSnapshot[statuses.Count];
            for (int i = 0; i < statuses.Count; i++)
            {
                snapshots[i] = new StatusSnapshot(statuses[i].Spec, statuses[i].Remaining, statuses[i].Stacks);
            }

            return snapshots;
        }

        public void Restore(IReadOnlyList<StatusSnapshot> snapshots)
        {
            Clear();
            if (snapshots == null)
            {
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                StatusSnapshot snapshot = snapshots[i];
                if (snapshot.Remaining <= 0f || string.IsNullOrEmpty(snapshot.Id))
                {
                    continue;
                }

                var instance = new CombatStatusInstance
                {
                    Spec = snapshot.ToSpec(),
                    Remaining = snapshot.Remaining,
                    Stacks = Math.Max(1, snapshot.Stacks),
                    TickTimer = 0f
                };
                statuses.Add(instance);
                Changed?.Invoke(instance, true);
            }
        }

        private bool HasKind(StatusKind kind)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private float RemainingOf(StatusKind kind)
        {
            float remaining = 0f;
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].Kind == kind)
                {
                    remaining = Math.Max(remaining, statuses[i].Remaining);
                }
            }

            return remaining;
        }
    }

    /// <summary>护盾池：先盾后血；到期自动消失。</summary>
    public sealed class ShieldPool
    {
        public float Amount { get; private set; }
        public float TimeRemaining { get; private set; }
        public bool HasShield => Amount > 0f;

        public event Action<float, float> Changed;

        public void Add(float amount, float duration)
        {
            if (amount <= 0f)
            {
                return;
            }

            float before = Amount;
            Amount += amount;
            TimeRemaining = Math.Max(TimeRemaining, Math.Max(0f, duration));
            Changed?.Invoke(Amount, Amount - before);
        }

        public float Absorb(float damage)
        {
            if (damage <= 0f || Amount <= 0f)
            {
                return 0f;
            }

            float absorbed = Math.Min(Amount, damage);
            Amount -= absorbed;
            if (Amount <= CombatMath.Epsilon)
            {
                Amount = 0f;
                TimeRemaining = 0f;
            }

            Changed?.Invoke(Amount, -absorbed);
            return absorbed;
        }

        public void Tick(float deltaTime)
        {
            if (TimeRemaining <= 0f)
            {
                return;
            }

            TimeRemaining -= deltaTime;
            if (TimeRemaining > 0f)
            {
                return;
            }

            float before = Amount;
            TimeRemaining = 0f;
            Amount = 0f;
            if (before > 0f)
            {
                Changed?.Invoke(0f, -before);
            }
        }

        public void Clear()
        {
            float before = Amount;
            Amount = 0f;
            TimeRemaining = 0f;
            if (before > 0f)
            {
                Changed?.Invoke(0f, -before);
            }
        }
    }
}
