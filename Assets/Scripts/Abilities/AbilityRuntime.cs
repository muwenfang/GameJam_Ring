using System;
using System.Collections.Generic;
using Ring.Characters;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 由「基础数值 + 五个能力槽」派生出来的最终战斗数值。
    /// 战斗层、UI、移动都只读这一份，避免同一套增益算两遍。
    /// </summary>
    public struct DerivedAbilityStats
    {
        public float MaxHealth;
        public float Defense;
        public float DamageReduction;
        public float Speed;
        public float MoveSpeedCellsPerSecond;
        public float JumpHeightCells;
        public float AttackIntervalSeconds;
        public float AttackWindupSeconds;
        public float AttackRangeCells;
        public float BasicAttackDamage;
        public float DamageDealtMultiplier;
        public float DamageTakenMultiplier;
        public float HealthRegenPerSecond;
        public bool Immobile;
        public bool Invulnerable;
        public bool CastCancelable;
        public float CastRemaining;

        public override string ToString()
        {
            return "HP " + AbilityMath.Number(MaxHealth)
                + " / 防御 " + AbilityMath.Number(Defense)
                + " (减伤 " + AbilityMath.Percent(DamageReduction) + ")"
                + " / 移速 " + AbilityMath.Number(MoveSpeedCellsPerSecond) + " 格每秒"
                + " / 攻击间隔 " + AbilityMath.Number(AttackIntervalSeconds) + "s"
                + " / 射程 " + AbilityMath.Number(AttackRangeCells) + " 格"
                + " / 平A 伤害 " + AbilityMath.Number(BasicAttackDamage);
        }
    }

    /// <summary>尝试施放一个主动能力的结果。治疗/护盾等即时效果由调用方落到角色身上。</summary>
    public readonly struct AbilityActivation
    {
        public AbilityActivation(
            bool success,
            AbilitySlot slot,
            AbilityCard card,
            AbilityDefinition definition,
            string failureReason,
            float healAmount,
            float shieldAmount,
            float shieldSeconds,
            float cooldownSeconds,
            float windupSeconds,
            float immobileSeconds,
            bool invulnerableDuring)
        {
            Success = success;
            Slot = slot;
            Card = card;
            Definition = definition;
            FailureReason = failureReason;
            HealAmount = healAmount;
            ShieldAmount = shieldAmount;
            ShieldSeconds = shieldSeconds;
            CooldownSeconds = cooldownSeconds;
            WindupSeconds = windupSeconds;
            ImmobileSeconds = immobileSeconds;
            InvulnerableDuring = invulnerableDuring;
        }

        public bool Success { get; }
        public AbilitySlot Slot { get; }
        public AbilityCard Card { get; }
        public AbilityDefinition Definition { get; }
        public string FailureReason { get; }
        public float HealAmount { get; }
        public float ShieldAmount { get; }
        public float ShieldSeconds { get; }
        public float CooldownSeconds { get; }
        public float WindupSeconds { get; }
        public float ImmobileSeconds { get; }
        public bool InvulnerableDuring { get; }

        public static AbilityActivation Reject(AbilitySlot slot, string reason)
        {
            return new AbilityActivation(false, slot, default, null, reason, 0f, 0f, 0f, 0f, 0f, 0f, false);
        }
    }

    /// <summary>一次平A命中目标后，由特质结算出来的附加效果（眩晕/击退/重箭）。</summary>
    public readonly struct BasicAttackHitResult
    {
        public BasicAttackHitResult(
            int targetId,
            float bonusDamage,
            bool consumedHeavyArrow,
            int windGraceStacks,
            bool triggeredCharge,
            float knockbackCells,
            bool stunned,
            float stunSeconds,
            float stunResistanceSeconds)
        {
            TargetId = targetId;
            BonusDamage = bonusDamage;
            ConsumedHeavyArrow = consumedHeavyArrow;
            WindGraceStacks = windGraceStacks;
            TriggeredCharge = triggeredCharge;
            KnockbackCells = knockbackCells;
            Stunned = stunned;
            StunSeconds = stunSeconds;
            StunResistanceSeconds = stunResistanceSeconds;
        }

        public int TargetId { get; }
        public float BonusDamage { get; }
        public bool ConsumedHeavyArrow { get; }
        public int WindGraceStacks { get; }
        public bool TriggeredCharge { get; }
        public float KnockbackCells { get; }
        public bool Stunned { get; }
        public float StunSeconds { get; }
        public float StunResistanceSeconds { get; }

        public override string ToString()
        {
            var parts = new List<string>(3);
            if (BonusDamage > 0f)
            {
                parts.Add("重箭 +" + AbilityMath.Number(BonusDamage));
            }

            if (KnockbackCells > 0f)
            {
                parts.Add("击退 " + AbilityMath.Number(KnockbackCells) + " 格");
            }

            if (Stunned)
            {
                parts.Add("眩晕 " + AbilityMath.Number(StunSeconds) + "s");
            }

            return parts.Count == 0 ? "无附加效果" : string.Join("、", parts);
        }
    }

    /// <summary>
    /// 角色身上的能力运行时：冷却、叠层、限时增益、特质计数、以及「属性衔接」的派生数值。
    ///
    /// 这个类是纯 C#（不引用 UnityEngine），因此可以离线跑测试；
    /// Unity 侧只需要每帧 Tick、读 <see cref="ComputeDerived"/> 去驱动移动/攻击/UI。
    ///
    /// 分工：
    ///   · <see cref="AbilityLoadout"/> 负责「我身上有哪些卡」；
    ///   · 本类负责「这些卡现在给我多少数值、什么时候能放、放了之后发生什么」；
    ///   · 角色实体（CharacterInstance）负责生命、位置、受击与死亡判定。
    /// </summary>
    public sealed class AbilityRuntime
    {
        private const int TimelineCapacity = 64;
        private const float TimelineSampleInterval = 0.1f;

        private struct HealthSample
        {
            public float Time;
            public float Health;
        }

        private struct TimedModifier
        {
            public ModifierKind Kind;
            public float Value;
            public float Remaining;
            public AbilitySlot Source;
        }

        private enum ModifierKind
        {
            DamageDealt,
            DamageTaken,
            MoveSpeed,
            AttackSpeed,
            Defense,
            DamageReduction,
            AttackIntervalOverride,
            AttackRangeOverride,
            HealthRegen
        }

        private sealed class TargetCounter
        {
            public int Count;
            public float LastHitTime;
            public float ImmuneUntil;
        }

        private readonly AbilityLoadout loadout;
        private readonly float[] cooldowns = new float[AbilitySlots.Count];
        private readonly List<TimedModifier> modifiers = new List<TimedModifier>(8);
        private readonly Dictionary<int, TargetCounter> strikeCounters = new Dictionary<int, TargetCounter>(4);
        private readonly HealthSample[] timeline = new HealthSample[TimelineCapacity];

        private IAbilityStats stats;
        private float time;
        private float maxHealthOverride = -1f;
        private float currentHealth;
        private int timelineCount;
        private int timelineHead;
        private float nextSampleTime;

        // 剑意
        private int swordIntentStacks;
        private float swordIntentTimer;

        // 风之优雅 / 重箭
        private int windGraceStacks;
        private float windGraceTimer;
        private bool heavyArrowReady;

        // 过载损耗
        private int hitsTaken;
        private bool chargeReady;

        // 施法期状态
        private float immobileRemaining;
        private float invulnerableRemaining;
        private bool castCancelable;
        private float regenAccumulator;

        public AbilityRuntime(AbilityLoadout loadout, IAbilityStats stats = null)
        {
            this.loadout = loadout ?? new AbilityLoadout();
            this.stats = stats;
        }

        public AbilityLoadout Loadout
        {
            get { return loadout; }
        }

        /// <summary>基础数值来源（通常是 CharacterBaseStats）。可以为 null，此时按 0 处理。</summary>
        public IAbilityStats Stats
        {
            get { return stats; }
            set { stats = value; }
        }

        public float MaxHealth
        {
            get
            {
                if (maxHealthOverride > 0f)
                {
                    return maxHealthOverride;
                }

                return stats != null ? stats.MaxHealth : 0f;
            }
        }

        public float CurrentHealth
        {
            get { return currentHealth; }
        }

        public float HealthRatio
        {
            get
            {
                float max = MaxHealth;
                return max <= 0f ? 0f : AbilityMath.Clamp01(currentHealth / max);
            }
        }

        public int SwordIntentStacks
        {
            get { return swordIntentStacks; }
        }

        public int WindGraceStacks
        {
            get { return windGraceStacks; }
        }

        public bool HeavyArrowReady
        {
            get { return heavyArrowReady; }
        }

        public bool ChargeReady
        {
            get { return chargeReady; }
        }

        public int HitsTaken
        {
            get { return hitsTaken; }
        }

        public float Time
        {
            get { return time; }
        }

        /// <summary>逆时触发（致死一击被撤销）。</summary>
        public event Action Rewound;

        /// <summary>某目标被眩晕：(目标 id, 秒)。</summary>
        public event Action<int, float> TargetStunned;

        /// <summary>某目标被击退：(目标 id, 格)。</summary>
        public event Action<int, float> TargetKnockedBack;

        public void SetHealth(float current, float max)
        {
            if (max > 0f)
            {
                maxHealthOverride = max;
            }

            currentHealth = AbilityMath.Clamp(current, 0f, Math.Max(0f, MaxHealth));
        }

        public void NotifyHealth(float current)
        {
            currentHealth = AbilityMath.Clamp(current, 0f, Math.Max(0f, MaxHealth));
        }

        /// <summary>新的一局：清冷却、清增益、清计数、清快照。</summary>
        public void Reset(float currentHealthValue, float maxHealthValue)
        {
            for (int i = 0; i < cooldowns.Length; i++)
            {
                cooldowns[i] = 0f;
            }

            modifiers.Clear();
            strikeCounters.Clear();
            swordIntentStacks = 0;
            swordIntentTimer = 0f;
            windGraceStacks = 0;
            windGraceTimer = 0f;
            heavyArrowReady = false;
            hitsTaken = 0;
            chargeReady = false;
            immobileRemaining = 0f;
            invulnerableRemaining = 0f;
            castCancelable = false;
            regenAccumulator = 0f;
            timelineCount = 0;
            timelineHead = 0;
            nextSampleTime = 0f;
            time = 0f;
            SetHealth(currentHealthValue, maxHealthValue);
            PushSample(currentHealthValue);
        }

        // ---------------- 冷却 ----------------

        public float GetCooldownRemaining(AbilitySlot slot)
        {
            int index = (int)slot;
            return index >= 0 && index < cooldowns.Length ? cooldowns[index] : 0f;
        }

        public float GetCooldownSeconds(AbilitySlot slot)
        {
            AbilityDefinition definition = loadout.GetDefinition(slot);
            if (definition == null)
            {
                return 0f;
            }

            if (definition.CooldownSeconds > 0f)
            {
                return definition.CooldownSeconds;
            }

            return slot == AbilitySlot.BasicAttack ? Math.Max(0.05f, ComputeDerived().AttackIntervalSeconds) : 0f;
        }

        public bool IsReady(AbilitySlot slot)
        {
            return loadout.Has(slot) && GetCooldownRemaining(slot) <= 0f;
        }

        // ---------------- 施放 ----------------

        /// <summary>
        /// 施放一个槽位。特质槽拒绝（特质是被动）。
        /// 成功后冷却已经开始走，限时增益也已经挂上；治疗/护盾数值在返回值里。
        /// </summary>
        public AbilityActivation TryActivate(AbilitySlot slot)
        {
            if (slot == AbilitySlot.Trait)
            {
                return AbilityActivation.Reject(slot, "特质是被动，不需要施放");
            }

            AbilityCard card;
            if (!loadout.TryGet(slot, out card))
            {
                return AbilityActivation.Reject(slot, "该槽没有能力");
            }

            AbilityDefinition definition = card.Definition;
            if (definition == null)
            {
                return AbilityActivation.Reject(slot, card.ToDisplayName() + " 没有对应数值");
            }

            float remaining = GetCooldownRemaining(slot);
            if (remaining > 0f)
            {
                return AbilityActivation.Reject(slot, card.ToDisplayName() + " 冷却中（还剩 " + AbilityMath.Number(remaining) + "s）");
            }

            float cooldown = GetCooldownSeconds(slot);
            cooldowns[(int)slot] = cooldown;

            float duration = Math.Max(0f, definition.DurationSeconds);
            float moveSpeedMultiplier = definition.MoveSpeedMultiplier;

            // 拆卡混装时「过载损耗」的移速×生命比例由派生计算统一处理，这里不重复乘。
            RegisterModifier(ModifierKind.MoveSpeed, moveSpeedMultiplier, duration, slot);
            RegisterModifier(ModifierKind.AttackSpeed, definition.AttackSpeedMultiplier, duration, slot);
            RegisterModifier(ModifierKind.Defense, definition.DefenseMultiplier, duration, slot);
            RegisterModifier(ModifierKind.DamageTaken, definition.DamageTakenMultiplier, duration, slot);
            RegisterModifier(ModifierKind.DamageReduction, definition.DamageReductionBonus, duration, slot);
            RegisterModifier(ModifierKind.AttackIntervalOverride, definition.AttackIntervalOverride, duration, slot);
            RegisterModifier(ModifierKind.AttackRangeOverride, definition.RangeCells, 0f, slot);
            RegisterModifier(ModifierKind.HealthRegen, definition.HealPerSecond, duration, slot);

            if (duration > 0f)
            {
                castCancelable = definition.CanCancelEarly;
                if (definition.InvulnerableDuring)
                {
                    invulnerableRemaining = Math.Max(invulnerableRemaining, duration);
                }
            }

            if (definition.InvulnerableDuring && duration <= 0f)
            {
                invulnerableRemaining = Math.Max(invulnerableRemaining, definition.ImmobileSeconds);
            }

            immobileRemaining = Math.Max(immobileRemaining, definition.ImmobileSeconds);

            return new AbilityActivation(
                true,
                slot,
                card,
                definition,
                null,
                Math.Max(0f, definition.HealAmount),
                Math.Max(0f, definition.ShieldAmount),
                Math.Max(0f, definition.ShieldSeconds),
                cooldown,
                Math.Max(0f, definition.WindupSeconds),
                Math.Max(0f, definition.ImmobileSeconds),
                definition.InvulnerableDuring);
        }

        /// <summary>施法被打断/主动取消时清掉施法期状态（限时增益保留，由 Tick 自然过期）。</summary>
        public void CancelCast()
        {
            immobileRemaining = 0f;
            invulnerableRemaining = 0f;
            castCancelable = false;
        }

        private void RegisterModifier(ModifierKind kind, float value, float duration, AbilitySlot source)
        {
            if (value <= 0f || duration <= 0f)
            {
                return;
            }

            if (kind == ModifierKind.MoveSpeed && AbilityMath.Approximately(value, 1f))
            {
                return;
            }

            modifiers.Add(new TimedModifier
            {
                Kind = kind,
                Value = value,
                Remaining = duration,
                Source = source
            });
        }

        // ---------------- 事件入口 ----------------

        /// <summary>造成一次伤害（平A 或任意技能各算一次）→ 剑意叠层。</summary>
        public void NotifyDamageDealt()
        {
            AbilityDefinition trait;
            if (!TryGetTraitDefinition(PersonaId.Swordsman, out trait))
            {
                return;
            }

            int maxStacks = Math.Max(1, trait.MaxStacks);
            swordIntentStacks = Math.Min(maxStacks, swordIntentStacks + 1);
            swordIntentTimer = trait.StackDurationSeconds > 0f ? trait.StackDurationSeconds : 2f;
        }

        /// <summary>
        /// 平A 命中一个目标 → 结算风之优雅/重箭、力量眩晕计数、过载损耗蓄势击退。
        /// 调用方按返回值处理伤害加成、击退与眩晕。
        /// </summary>
        public BasicAttackHitResult NotifyBasicAttackHit(int targetId)
        {
            float bonusDamage = 0f;
            bool consumedHeavy = false;
            bool triggeredCharge = false;
            float knockback = 0f;
            bool stunned = false;
            float stunSeconds = 0f;
            float stunResistance = 0f;

            AbilityDefinition rangerTrait;
            if (TryGetTraitDefinition(PersonaId.Ranger, out rangerTrait))
            {
                if (heavyArrowReady)
                {
                    heavyArrowReady = false;
                    windGraceStacks = 0;
                    windGraceTimer = 0f;
                    bonusDamage = Math.Max(0f, rangerTrait.StackAttackDamageBonus);
                    consumedHeavy = true;
                }
                else
                {
                    int maxStacks = Math.Max(1, rangerTrait.MaxStacks);
                    windGraceStacks = Math.Min(maxStacks, windGraceStacks + 1);
                    windGraceTimer = rangerTrait.StackDurationSeconds > 0f ? rangerTrait.StackDurationSeconds : 2f;
                    if (windGraceStacks >= maxStacks)
                    {
                        heavyArrowReady = true;
                    }
                }
            }

            AbilityDefinition mechanicTrait;
            if (TryGetTraitDefinition(PersonaId.SteamMechanic, out mechanicTrait) && chargeReady)
            {
                chargeReady = false;
                knockback = Math.Max(0f, mechanicTrait.KnockbackCells);
                triggeredCharge = knockback > 0f;
            }

            AbilityDefinition strongmanTrait;
            if (TryGetTraitDefinition(PersonaId.Strongman, out strongmanTrait))
            {
                TargetCounter counter;
                if (!strikeCounters.TryGetValue(targetId, out counter))
                {
                    counter = new TargetCounter();
                    strikeCounters[targetId] = counter;
                }

                if (counter.ImmuneUntil <= time)
                {
                    counter.LastHitTime = time;
                    counter.Count++;

                    int required = Math.Max(1, strongmanTrait.RequiredHitsForTrigger);
                    if (counter.Count >= required)
                    {
                        counter.Count = 0;
                        stunned = true;
                        stunSeconds = Math.Max(0f, strongmanTrait.StunSeconds);
                        stunResistance = Math.Max(0f, strongmanTrait.StunResistanceSeconds);
                        counter.ImmuneUntil = time + stunSeconds + stunResistance;
                    }
                }
            }

            if (knockback > 0f && TargetKnockedBack != null)
            {
                TargetKnockedBack(targetId, knockback);
            }

            if (stunned && TargetStunned != null)
            {
                TargetStunned(targetId, stunSeconds);
            }

            return new BasicAttackHitResult(
                targetId,
                bonusDamage,
                consumedHeavy,
                windGraceStacks,
                triggeredCharge,
                knockback,
                stunned,
                stunSeconds,
                stunResistance);
        }

        /// <summary>受到一次伤害 → 过载损耗的「受击 3 次蓄势」。</summary>
        public void NotifyDamageTaken(float appliedDamage)
        {
            if (appliedDamage <= 0f)
            {
                return;
            }

            AbilityDefinition mechanicTrait;
            if (!TryGetTraitDefinition(PersonaId.SteamMechanic, out mechanicTrait))
            {
                return;
            }

            int required = Math.Max(1, mechanicTrait.RequiredHitsForTrigger);
            hitsTaken++;
            if (hitsTaken >= required)
            {
                hitsTaken = 0;
                chargeReady = true;
            }
        }

        /// <summary>
        /// 致死一击的拦截：持有「逆时」且冷却就绪时，把生命倒回 3 秒前并消耗冷却。
        /// 位置与增益的回滚由调用方按 <see cref="RewindSeconds"/> 处理。
        /// </summary>
        public bool TryConsumeLethalHit(out float restoredHealth)
        {
            restoredHealth = currentHealth;

            AbilityDefinition trait;
            if (!TryGetTraitDefinition(PersonaId.Clockmaker, out trait))
            {
                return false;
            }

            if (GetCooldownRemaining(AbilitySlot.Trait) > 0f)
            {
                return false;
            }

            if (!TrySampleRewind(RewindSeconds, out restoredHealth))
            {
                restoredHealth = currentHealth;
                return false;
            }

            cooldowns[(int)AbilitySlot.Trait] = Math.Max(0f, trait.CooldownSeconds);
            swordIntentStacks = 0;
            swordIntentTimer = 0f;
            windGraceStacks = 0;
            windGraceTimer = 0f;
            heavyArrowReady = false;
            modifiers.Clear();
            currentHealth = AbilityMath.Clamp(restoredHealth, 0f, Math.Max(0f, MaxHealth));

            if (Rewound != null)
            {
                Rewound();
            }

            return true;
        }

        /// <summary>逆时回滚的时长（秒）：读特质上的 DurationSeconds，缺省 3 秒。</summary>
        public float RewindSeconds
        {
            get
            {
                AbilityDefinition trait;
                if (TryGetTraitDefinition(PersonaId.Clockmaker, out trait) && trait.DurationSeconds > 0f)
                {
                    return trait.DurationSeconds;
                }

                return 3f;
            }
        }

        /// <summary>取最近一次「3 秒前」的生命快照，供回滚用。</summary>
        public bool TrySampleRewind(float secondsAgo, out float health)
        {
            health = currentHealth;
            if (timelineCount == 0)
            {
                return false;
            }

            float target = time - Math.Max(0.1f, secondsAgo);
            bool found = false;
            float bestDelta = float.MaxValue;
            float bestHealth = currentHealth;

            for (int i = 0; i < timelineCount; i++)
            {
                int index = (timelineHead - 1 - i + TimelineCapacity * 2) % TimelineCapacity;
                HealthSample sample = timeline[index];
                float delta = Math.Abs(sample.Time - target);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestHealth = sample.Health;
                    found = true;
                }
            }

            if (found)
            {
                health = bestHealth;
            }

            return found;
        }

        // ---------------- 每帧推进 ----------------

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            time += deltaTime;

            for (int i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i] > 0f)
                {
                    cooldowns[i] = Math.Max(0f, cooldowns[i] - deltaTime);
                }
            }

            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                TimedModifier modifier = modifiers[i];
                modifier.Remaining -= deltaTime;
                if (modifier.Remaining <= 0f)
                {
                    modifiers.RemoveAt(i);
                }
                else
                {
                    modifiers[i] = modifier;
                }
            }

            if (swordIntentStacks > 0)
            {
                swordIntentTimer -= deltaTime;
                if (swordIntentTimer <= 0f)
                {
                    swordIntentStacks = 0;
                }
            }

            if (windGraceStacks > 0)
            {
                windGraceTimer -= deltaTime;
                if (windGraceTimer <= 0f)
                {
                    windGraceStacks = 0;
                    heavyArrowReady = false;
                }
            }

            immobileRemaining = Math.Max(0f, immobileRemaining - deltaTime);
            invulnerableRemaining = Math.Max(0f, invulnerableRemaining - deltaTime);
            if (immobileRemaining <= 0f)
            {
                castCancelable = false;
            }

            ExpireStrikeCounters();

            float regen = ComputeDerived().HealthRegenPerSecond;
            if (regen > 0f && currentHealth > 0f)
            {
                regenAccumulator += regen * deltaTime;
            }

            if (time >= nextSampleTime)
            {
                nextSampleTime = time + TimelineSampleInterval;
                PushSample(currentHealth);
            }
        }

        private void ExpireStrikeCounters()
        {
            if (strikeCounters.Count == 0)
            {
                return;
            }

            AbilityDefinition trait;
            TryGetTraitDefinition(PersonaId.Strongman, out trait);
            float window = trait != null && trait.TriggerWindowSeconds > 0f ? trait.TriggerWindowSeconds : 1.5f;

            List<int> expired = null;
            foreach (KeyValuePair<int, TargetCounter> pair in strikeCounters)
            {
                TargetCounter counter = pair.Value;
                if (counter.Count > 0 && time - counter.LastHitTime > window)
                {
                    counter.Count = 0;
                }

                if (counter.Count == 0 && counter.ImmuneUntil <= time)
                {
                    if (expired == null)
                    {
                        expired = new List<int>(2);
                    }

                    expired.Add(pair.Key);
                }
            }

            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                {
                    strikeCounters.Remove(expired[i]);
                }
            }
        }

        /// <summary>取走累计的治疗量（斯巴达韧性、沉腰调息等持续回复）。</summary>
        public float ConsumeRegen()
        {
            float value = regenAccumulator;
            regenAccumulator = 0f;
            return value;
        }

        private void PushSample(float health)
        {
            timeline[timelineHead] = new HealthSample { Time = time, Health = health };
            timelineHead = (timelineHead + 1) % TimelineCapacity;
            if (timelineCount < TimelineCapacity)
            {
                timelineCount++;
            }
        }

        // ---------------- 派生数值 ----------------

        /// <summary>
        /// 计算最终战斗数值：基础值 → 特质被动 → 限时增益 → 生命比例等联动。
        /// 每帧调用也够快（内部只有几次循环）。
        /// </summary>
        public DerivedAbilityStats ComputeDerived()
        {
            var derived = new DerivedAbilityStats();

            float maxHealth = MaxHealth;
            derived.MaxHealth = maxHealth;
            derived.Defense = stats != null ? stats.Defense : 0f;
            derived.DamageReduction = stats != null ? stats.DamageReduction : 0f;
            derived.Speed = stats != null ? stats.Speed : 0f;
            derived.MoveSpeedCellsPerSecond = stats != null ? stats.MoveSpeedCellsPerSecond : 0f;
            derived.JumpHeightCells = stats != null ? stats.JumpHeightCells : 0f;
            derived.AttackIntervalSeconds = stats != null ? stats.AttackIntervalSeconds : 0f;

            float moveMultiplier = 1f;
            float attackSpeedMultiplier = 1f;
            float damageMultiplier = 1f;
            float damageTakenMultiplier = 1f;
            float defenseMultiplier = 1f;
            float defenseFlat = 0f;
            float reductionAdd = 0f;
            float regen = 0f;
            float intervalOverride = 0f;
            float rangeOverride = 0f;
            bool speedScalesWithHealth = false;

            AbilityDefinition basicAttack = loadout.GetDefinition(AbilitySlot.BasicAttack);
            if (basicAttack != null)
            {
                derived.BasicAttackDamage = basicAttack.Damage > 0f ? basicAttack.Damage : basicAttack.PelletDamage;
                derived.AttackWindupSeconds = basicAttack.WindupSeconds;
                derived.AttackRangeCells = Math.Max(basicAttack.RangeCells, basicAttack.AreaLengthCells);
            }

            if (loadout.Has(AbilitySlot.BasicAttack))
            {
                AbilityCard basicCard;
                loadout.TryGet(AbilitySlot.BasicAttack, out basicCard);
                if (basicCard.Definition != null)
                {
                    if (basicCard.Definition.BasicAttackDamageOverride > 0f)
                    {
                        derived.BasicAttackDamage = basicCard.Definition.BasicAttackDamageOverride;
                    }
                }
            }

            // 特质被动。拆卡混装时，只要特质槽里真的是某个人设的特质卡，被动就生效。
            IReadOnlyList<AbilityCard> traits = loadout.GetTraitCards();
            for (int i = 0; i < traits.Count; i++)
            {
                AbilityCard card = traits[i];
                AbilityDefinition definition = card.Definition;
                if (definition == null)
                {
                    continue;
                }

                switch (card.Persona)
                {
                    case PersonaId.Swordsman:
                        damageMultiplier *= 1f + (swordIntentStacks * Math.Max(0f, definition.StackDamageBonus));
                        break;

                    case PersonaId.SteamMechanic:
                        if (definition.MoveSpeedScalesWithHealth)
                        {
                            speedScalesWithHealth = true;
                        }

                        break;

                    case PersonaId.Spartan:
                        defenseFlat += definition.DefenseFlatBonus;
                        regen += definition.HealPerSecond;
                        break;

                    case PersonaId.Ranger:
                        rangeOverride = Math.Max(rangeOverride, definition.RangeCells);
                        moveMultiplier *= 1f + (windGraceStacks * Math.Max(0f, definition.StackMoveSpeedBonus));
                        break;
                }
            }

            for (int i = 0; i < modifiers.Count; i++)
            {
                TimedModifier modifier = modifiers[i];
                switch (modifier.Kind)
                {
                    case ModifierKind.DamageDealt:
                        damageMultiplier *= modifier.Value;
                        break;

                    case ModifierKind.DamageTaken:
                        damageTakenMultiplier *= modifier.Value;
                        break;

                    case ModifierKind.MoveSpeed:
                        moveMultiplier *= modifier.Value;
                        break;

                    case ModifierKind.AttackSpeed:
                        attackSpeedMultiplier *= modifier.Value;
                        break;

                    case ModifierKind.Defense:
                        defenseMultiplier *= modifier.Value;
                        break;

                    case ModifierKind.DamageReduction:
                        reductionAdd += modifier.Value;
                        break;

                    case ModifierKind.AttackIntervalOverride:
                        intervalOverride = intervalOverride <= 0f ? modifier.Value : Math.Min(intervalOverride, modifier.Value);
                        break;

                    case ModifierKind.AttackRangeOverride:
                        rangeOverride = Math.Max(rangeOverride, modifier.Value);
                        break;

                    case ModifierKind.HealthRegen:
                        regen += modifier.Value;
                        break;
                }
            }

            if (speedScalesWithHealth)
            {
                moveMultiplier *= HealthRatio;
            }

            derived.Defense = Math.Max(0f, (derived.Defense + defenseFlat) * defenseMultiplier);
            derived.DamageReduction = AbilityMath.Clamp01(derived.DamageReduction + reductionAdd);
            derived.MoveSpeedCellsPerSecond *= moveMultiplier;
            derived.DamageDealtMultiplier = damageMultiplier;
            derived.DamageTakenMultiplier = damageTakenMultiplier;
            derived.HealthRegenPerSecond = regen;
            derived.AttackRangeCells = Math.Max(derived.AttackRangeCells, rangeOverride);

            if (intervalOverride > 0f)
            {
                derived.AttackIntervalSeconds = intervalOverride;
            }
            else if (attackSpeedMultiplier > 0f && !AbilityMath.Approximately(attackSpeedMultiplier, 1f))
            {
                derived.AttackIntervalSeconds /= attackSpeedMultiplier;
            }

            derived.Immobile = immobileRemaining > 0f;
            derived.Invulnerable = invulnerableRemaining > 0f;
            derived.CastCancelable = castCancelable;
            derived.CastRemaining = immobileRemaining;

            return derived;
        }

        // ---------------- 工具 ----------------

        private bool TryGetTraitDefinition(PersonaId persona, out AbilityDefinition definition)
        {
            definition = null;
            IReadOnlyList<AbilityCard> traits = loadout.GetTraitCards();
            for (int i = 0; i < traits.Count; i++)
            {
                if (traits[i].Persona == persona)
                {
                    definition = traits[i].Definition;
                    return definition != null;
                }
            }

            return false;
        }

        /// <summary>把基础数值（来自 CharacterBaseStats 或文档快照）同步进来。</summary>
        public void SyncStatsFrom(IAbilityStats source)
        {
            if (source != null)
            {
                stats = source;
            }
        }

        public string DescribeState()
        {
            var parts = new List<string>(6);
            parts.Add("HP " + AbilityMath.Number(currentHealth) + "/" + AbilityMath.Number(MaxHealth));

            if (swordIntentStacks > 0)
            {
                parts.Add("剑意 " + swordIntentStacks + " 层");
            }

            if (windGraceStacks > 0)
            {
                parts.Add("风之优雅 " + windGraceStacks + " 层");
            }

            if (heavyArrowReady)
            {
                parts.Add("重箭就绪");
            }

            if (chargeReady)
            {
                parts.Add("蓄势就绪");
            }

            if (hitsTaken > 0)
            {
                parts.Add("受击 " + hitsTaken + " 次");
            }

            if (modifiers.Count > 0)
            {
                parts.Add("增益 " + modifiers.Count + " 项");
            }

            if (immobileRemaining > 0f)
            {
                parts.Add("定身 " + AbilityMath.Number(immobileRemaining) + "s");
            }

            if (invulnerableRemaining > 0f)
            {
                parts.Add("无敌 " + AbilityMath.Number(invulnerableRemaining) + "s");
            }

            return string.Join(" ｜ ", parts);
        }

        public void CollectIssues(ValidationReport report, string context = null)
        {
            if (report == null)
            {
                return;
            }

            string where = string.IsNullOrEmpty(context) ? "能力运行时" : context;
            loadout.CollectIssues(report, where);

            if (loadout.Has(AbilitySlot.BasicAttack) && loadout.GetDefinition(AbilitySlot.BasicAttack) == null)
            {
                report.Error(where, "普攻槽有卡但没有数值定义");
            }

            if (stats == null)
            {
                report.Warn(where, "没有绑定基础数值（IAbilityStats），派生数值会全部按 0 计算");
            }
        }
    }
}
