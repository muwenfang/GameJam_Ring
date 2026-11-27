using System;
using Ring.Abilities;

namespace Ring.Combat
{
    /// <summary>
    /// 一个角色的战斗状态聚合：增益/减益 + 护盾 + 姿态，并作为 AbilityRuntime 的外部修正来源。
    /// </summary>
    public sealed class CombatantState : IAbilityExternalStats
    {
        private bool suppressEvents;

        public CombatantState(int ownerId = 0)
        {
            OwnerId = ownerId;
            Status.Changed += HandleStatusChanged;
            Shield.Changed += HandleShieldChanged;
            Stance.Ended += HandleStanceEnded;
        }

        public int OwnerId { get; set; }
        public StatusRuntime Status { get; } = new StatusRuntime();
        public ShieldPool Shield { get; } = new ShieldPool();
        public CombatStanceRuntime Stance { get; } = new CombatStanceRuntime();

        public float MoveSpeedMultiplier => Status.MoveSpeedMultiplier * Stance.MoveSpeedMultiplier;
        public float AttackSpeedMultiplier => Status.AttackSpeedMultiplier;
        public float DamageDealtMultiplier => Status.DamageDealtMultiplier;
        public float DamageTakenMultiplier => Status.DamageTakenMultiplier * Stance.DamageTakenMultiplier;
        public float DefenseMultiplier => Status.DefenseMultiplier;
        public float DamageReductionBonus => 0f;
        public bool Immobile => Status.IsImmobile || Stance.Immobile;
        public bool Invulnerable => false;

        public bool CanBasicAttack => !Stance.DisableBasicAttack && !Status.IsStunned;
        public bool CanCastSkill(AbilitySlot slot)
        {
            if (Status.IsStunned)
            {
                return false;
            }

            if (Stance.DisableAllSkills)
            {
                return false;
            }

            if (slot == AbilitySlot.Id && Stance.DisableIdSkill)
            {
                return false;
            }

            return true;
        }

        public bool KnockbackImmune => Stance.KnockbackImmune;

        public StatusApplyResult ApplyStatus(CombatStatusSpec spec) => Status.Apply(spec);

        public int CleanseDebuffs(int max = 0) => Status.CleanseDebuffs(max);

        public void AddShield(float amount, float duration) => Shield.Add(amount, duration);

        public float AbsorbShield(float damage) => Shield.Absorb(damage);

        public bool BeginStance(string id, float durationOverride = 0f, float endLagDurationOverride = 0f)
        {
            CombatStanceSpec spec = CombatStances.Get(id);
            if (spec == null)
            {
                return false;
            }

            Stance.Begin(spec, durationOverride, endLagDurationOverride);
            RaiseBuff(spec.Id, true, 1);
            return true;
        }

        public void CancelStance()
        {
            string id = Stance.CurrentId;
            Stance.Cancel();
            if (!string.IsNullOrEmpty(id))
            {
                RaiseBuff(id, false, 0);
            }
        }

        public void NotifyHit()
        {
            Stance.NotifyHit();
        }

        public void Tick(float deltaTime, Action<float, int> onDotTick)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Status.Tick(deltaTime, onDotTick);
            Shield.Tick(deltaTime);
            Stance.Tick(deltaTime);
        }

        public void Reset()
        {
            suppressEvents = true;
            Status.Clear();
            Shield.Clear();
            Stance.Cancel();
            suppressEvents = false;
        }

        private void HandleStatusChanged(CombatStatusInstance status, bool added)
        {
            if (status == null)
            {
                return;
            }

            RaiseBuff(status.Id, added, status.Stacks);
        }

        private void HandleShieldChanged(float current, float delta)
        {
            if (suppressEvents || Math.Abs(delta) <= CombatMath.Epsilon)
            {
                return;
            }

            CombatEvents.RaiseShieldChanged(new ShieldEvent(OwnerId, current, delta));
        }

        private void HandleStanceEnded(CombatStanceSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            RaiseBuff(spec.Id, false, 0);
            if (spec.ShieldOnPerfectEnd && Stance.EndedWithoutBeingHit)
            {
                Shield.Add(spec.PerfectShieldAmount, spec.PerfectShieldSeconds);
            }
        }

        private void RaiseBuff(string id, bool added, int stacks)
        {
            if (suppressEvents || string.IsNullOrEmpty(id))
            {
                return;
            }

            CombatEvents.RaiseBuffChanged(new BuffChangedEvent(OwnerId, id, added, stacks));
        }
    }
}
