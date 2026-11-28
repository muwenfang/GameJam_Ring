using System;
using System.Collections.Generic;
using Ring.Abilities;
using Ring.Bells;
using Ring.Characters;
using Ring.Combat;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 场上一个角色实例：把「基础数值资产」「钟声参与者」「血量」三件事绑在一个 GameObject 上。
    ///
    /// 战斗层：伤害统一走 DamagePipeline（防御→乘区→护盾→致死拦截），
    /// Buff/姿态/护盾汇总到 CombatantState，0.1s 采样一次 CharacterSnapshot 供逆时/回溯使用。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/角色实例 CharacterInstance")]
    public sealed class CharacterInstance : MonoBehaviour
    {
        [Header("身份")]
        [SerializeField] private int participantId;
        [SerializeField] private bool isPlayer;
        [SerializeField] private PersonaId persona = PersonaId.Swordsman;

        /// <summary>已构建形象所用的人设；和「特质决定的人设」不一致时重建视觉。</summary>
        private PersonaId visualPersona = PersonaId.Swordsman;
        private bool visualBuilt;

        [Header("血量（运行时）")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;

        private bool deathRaised;

        private AbilityLoadout loadout;
        private AbilityRuntime abilities;
        private readonly CombatantState combat = new CombatantState();
        private readonly SnapshotRing snapshots = new SnapshotRing();
        private readonly DamagePipeline damagePipeline = new DamagePipeline();

        private float combatTime;
        private float invulnerableUntil;
        private float stunResistanceUntil;
        private float controlImmuneUntil;
        private float nextSnapshotTime;
        private CombatBody body;

        /// <summary>死亡事件（只触发一次）。</summary>
        public event Action<CharacterInstance> Died;

        /// <summary>血量变化事件，HUD 用。</summary>
        public event Action<CharacterInstance> HealthChanged;

        public int Id
        {
            get { return participantId; }
        }

        public bool IsPlayer
        {
            get { return isPlayer; }
        }

        /// <summary>基础数值资产（生命、防御、移速等的来源）。</summary>
        public CharacterBaseStats Stats { get; private set; }

        /// <summary>钟声系统的参与者视图（手上有什么牌都在这里）。</summary>
        public BellParticipant Participant { get; private set; }

        /// <summary>
        /// 五个能力槽：特质 / 普攻 / 本我 / 自我 / 超我。
        /// 钟声只管特质与三张技能卡，普攻卡在生成时直接给，但三者都落到这一张装备表里。
        /// </summary>
        public AbilityLoadout Loadout
        {
            get { return loadout; }
        }

        /// <summary>能力运行时：冷却、叠层、限时增益、生命快照。</summary>
        public AbilityRuntime Abilities
        {
            get { return abilities; }
        }

        /// <summary>Buff/护盾/姿态聚合。</summary>
        public CombatantState Combat
        {
            get { return combat; }
        }

        /// <summary>3 秒状态快照环（0.1s 采样）。</summary>
        public SnapshotRing Snapshots
        {
            get { return snapshots; }
        }

        public CombatBody Body
        {
            get
            {
                if (body == null)
                {
                    body = GetComponent<CombatBody>();
                }

                return body;
            }
        }

        /// <summary>当前派生战斗数值（已含特质被动、限时增益、Buff 与姿态）。</summary>
        public DerivedAbilityStats Derived
        {
            get { return abilities != null ? abilities.ComputeDerived() : default(DerivedAbilityStats); }
        }

        /// <summary>移动速度（格/秒），已含「过载损耗」「风之优雅」「减速」「举盾」等修正。</summary>
        public float MoveSpeedCellsPerSecond
        {
            get
            {
                if (abilities != null)
                {
                    return abilities.ComputeDerived().MoveSpeedCellsPerSecond;
                }

                return Stats != null ? Stats.MoveSpeedCellsPerSecond : 0f;
            }
        }

        /// <summary>是否处于无敌（绝对防御、闪避无敌帧等）。</summary>
        public bool IsInvulnerable
        {
            get
            {
                if (combatTime < invulnerableUntil)
                {
                    return true;
                }

                CombatBody combatBody = Body;
                if (combatBody != null && combatBody.HasDodgeInvulnerability)
                {
                    return true;
                }

                return abilities != null && abilities.ComputeDerived().Invulnerable;
            }
        }

        /// <summary>霸体：免疫眩晕/定身/减速，但不等同于无敌。</summary>
        public bool IsControlImmune => combatTime < controlImmuneUntil;

        public PersonaId Persona
        {
            get { return Stats != null ? Stats.Persona : persona; }
        }

        public string DisplayName
        {
            get { return Participant != null ? Participant.DisplayName : name; }
        }

        public float MaxHealth
        {
            get { return maxHealth; }
        }

        public float CurrentHealth
        {
            get { return currentHealth; }
        }

        public float HealthRatio
        {
            get { return maxHealth <= 0f ? 0f : Mathf.Clamp01(currentHealth / maxHealth); }
        }

        public bool IsDead
        {
            get { return currentHealth <= 0f || deathRaised; }
        }

        /// <summary>是否已凑出一套完整人设（B13/B14 的判定口径）。</summary>
        public bool HasCompleteSet
        {
            get { return Participant != null && Participant.HasCompleteSet; }
        }

        public PersonaId CompletedPersona
        {
            get { return Participant != null ? Participant.CompletedPersona : default; }
        }

        /// <summary>
        /// 特质决定的人设：手上持有哪个人设的特质，就是哪个人设。
        /// 多张特质时取最后获得的那张（夺取别人的特质立刻改变你的身份）；
        /// 一张特质都没有时退回剑士。
        /// </summary>
        public PersonaId TraitPersona
        {
            get
            {
                if (Participant != null && Participant.Traits.Count > 0)
                {
                    return Participant.Traits[Participant.Traits.Count - 1].Persona;
                }

                return PersonaId.Swordsman;
            }
        }

        /// <summary>当前形象实际使用的人设（没有专属序列帧的人设统一借剑士占位）。</summary>
        public PersonaId VisualPersona
        {
            get { return ResolveVisualPersona(TraitPersona); }
        }

        /// <summary>
        /// 目前剑士/斯巴达/力士/精灵游侠有序列帧；其余人设暂时借剑士当占位。
        /// 新人设补齐美术后，在 RefreshVisualAnimation 里接上组件、再把这里放行即可。
        /// </summary>
        public static PersonaId ResolveVisualPersona(PersonaId source)
        {
            switch (source)
            {
                case PersonaId.Swordsman:
                case PersonaId.Spartan:
                case PersonaId.Strongman:
                case PersonaId.Ranger:
                    return source;
                default:
                    return PersonaId.Swordsman;
            }
        }

        /// <summary>由生成器调用：绑定 id、数值资产与是否玩家。</summary>
        public void Initialize(int id, CharacterBaseStats stats, bool player)
        {
            participantId = id;
            Stats = stats;
            isPlayer = player;
            persona = stats != null ? stats.Persona : PersonaId.Swordsman;
            deathRaised = false;

            maxHealth = stats != null ? stats.MaxHealth : 100f;
            currentHealth = maxHealth;

            string displayName = stats != null ? stats.DisplayName : ("角色" + id);
            gameObject.name = (player ? "玩家_" : "人机_") + displayName;
            Participant = new BellParticipant(id, displayName);

            loadout = new AbilityLoadout();
            loadout.TryAdd(new AbilityCard(persona, AbilitySlot.BasicAttack), out _);
            abilities = new AbilityRuntime(loadout, new CharacterStatAdapter(stats));
            abilities.SetHealth(currentHealth, maxHealth);
            abilities.ExternalStats = combat;
            combat.OwnerId = id;
            combat.Reset();
            snapshots.Clear();
            combatTime = 0f;
            invulnerableUntil = 0f;
            stunResistanceUntil = 0f;
            controlImmuneUntil = 0f;
            nextSnapshotTime = 0f;
            RefreshLoadout();

            BuildVisual();
            CaptureSnapshot();
        }

        private void Update()
        {
            if (abilities == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            combatTime += deltaTime;
            abilities.Tick(deltaTime);
            combat.Tick(deltaTime, HandleDotTick);
            abilities.NotifyHealth(currentHealth);

            if (!IsDead)
            {
                // 斯巴达韧性、沉腰调息这类持续回血由运行时累计，这里统一落到身上。
                float regen = abilities.ConsumeRegen();
                if (regen > 0f)
                {
                    HealSilently(regen);
                }
            }

            if (combatTime >= nextSnapshotTime)
            {
                CaptureSnapshot();
                nextSnapshotTime = combatTime + snapshots.Interval;
            }
        }

        // ---------------- 伤害 ----------------

        /// <summary>兼容旧调用：无攻击者、无标签的普通伤害。</summary>
        public void TakeDamage(float rawDamage)
        {
            TakeDamage(new DamageRequest
            {
                TargetId = participantId,
                RawDamage = rawDamage,
                Tags = DamageTags.None
            });
        }

        /// <summary>
        /// 统一受击入口：防御减伤 → 造成/承受乘区 → 护盾 → 致死判定（逆时在这里拦截）。
        /// </summary>
        public DamageResult TakeDamage(DamageRequest request)
        {
            var result = new DamageResult();
            if (request == null || request.RawDamage <= 0f || IsDead)
            {
                return result;
            }

            request.TargetId = participantId;

            if (abilities == null)
            {
                float plain = Stats != null ? Stats.MitigateDamage(request.RawDamage) : request.RawDamage;
                result.HealthDamage = Mathf.Max(0f, plain);
                currentHealth = Mathf.Max(0f, currentHealth - result.HealthDamage);
                RaiseHealthChanged();
                RaiseDamagedEvent(request, result);
                if (currentHealth <= 0f)
                {
                    RaiseDied(request.AttackerId);
                }

                return result;
            }

            DerivedAbilityStats derived = abilities.ComputeDerived();

            // 资产里的 DamageReduction 只是「基础防御」的预换算值；防御被增益/削防改动后，
            // 这里按同一策略重算，并把只来自增益的额外减伤保留下来，避免重复计算。
            float baseReduction = Stats != null
                ? CharacterBalance.ReductionFor(Persona, Stats.Defense)
                : 0f;
            float defenseReduction = CharacterBalance.ReductionFor(Persona, derived.Defense);
            float damageReduction = Mathf.Clamp01(
                defenseReduction + derived.DamageReduction - baseReduction);

            // 举盾只挡正面 180°：背后的攻击不能吃到姿态的 0.1 倍率。
            float incomingMultiplier = derived.DamageTakenMultiplier;
            if (IsFrontalOnlyBackHit(request))
            {
                float stanceMultiplier = combat.Stance.DamageTakenMultiplier;
                if (stanceMultiplier > 0.0001f)
                {
                    incomingMultiplier /= stanceMultiplier;
                }
            }

            var stats = new DamageStats
            {
                Defense = derived.Defense,
                DamageReduction = damageReduction,
                OutgoingMultiplier = Mathf.Max(0f, request.OutgoingMultiplier),
                IncomingMultiplier = Mathf.Max(0f, incomingMultiplier),
                Shield = combat.Shield.Amount,
                Invulnerable = IsInvulnerable
            };

            result = damagePipeline.Resolve(request, stats, currentHealth, TryInterceptLethal);
            if (result.ShieldAbsorbed > 0f)
            {
                combat.Shield.Absorb(result.ShieldAbsorbed);
            }

            if (result.HealthDamage > 0f || result.ShieldAbsorbed > 0f)
            {
                // 被护盾完全吸收也算「挨打」：调息判定与受击反馈都依赖这一下。
                combat.NotifyHit();
                ApplyKnockback(request);
            }

            if (result.Blocked)
            {
                RaiseDamagedEvent(request, result);
                return result;
            }

            if (result.HealthDamage > 0f)
            {
                currentHealth = Mathf.Max(0f, currentHealth - result.HealthDamage);
                abilities.NotifyDamageTaken(result.HealthDamage);
                abilities.NotifyHealth(currentHealth);
                RaiseHealthChanged();
            }

            RaiseDamagedEvent(request, result);

            if (currentHealth <= 0f)
            {
                RaiseDied(request.AttackerId);
            }

            return result;
        }

        private bool TryInterceptLethal()
        {
            if (abilities == null)
            {
                return false;
            }

            float restored;
            if (!abilities.TryConsumeLethalHit(out restored))
            {
                return false;
            }

            if (!Rewind(abilities.RewindSeconds, StateRestoreMode.Full))
            {
                ApplyRestoredHealth(restored);
            }

            return true;
        }

        private bool IsFrontalOnlyBackHit(DamageRequest request)
        {
            if (combat == null || !combat.Stance.IsActive)
            {
                return false;
            }

            CombatStanceSpec stance = combat.Stance.Current;
            if (stance == null || !stance.FrontalOnly)
            {
                return false;
            }

            if (request.Direction.SqrMagnitude <= CombatMath.Epsilon)
            {
                return false;
            }

            Vec2 facing = Body != null ? Body.FacingVector : Vec2.Right;
            Vec2 toSource = new Vec2(-request.Direction.X, -request.Direction.Y);
            return Vec2.Dot(toSource.Normalized, facing) < 0f;
        }

        private void ApplyKnockback(DamageRequest request)
        {
            if (request.KnockbackCells <= 0f || combat.KnockbackImmune)
            {
                return;
            }

            CombatBody combatBody = Body;
            if (combatBody == null)
            {
                return;
            }

            Vec2 direction = request.Direction;
            if (direction.SqrMagnitude <= CombatMath.Epsilon)
            {
                direction = -combatBody.FacingVector;
            }

            combatBody.ApplyKnockback(direction, request.KnockbackCells);
        }

        private void RaiseDamagedEvent(DamageRequest request, DamageResult result)
        {
            CombatEvents.RaiseDamaged(new DamageEvent(
                request.AttackerId,
                participantId,
                result.HealthDamage,
                result.ShieldAbsorbed,
                request.Tags,
                result.Killed || result.LethalIntercepted,
                result.Blocked));
        }

        private void HandleDotTick(float amount, int sourceId)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            TakeDamage(new DamageRequest
            {
                AttackerId = sourceId,
                TargetId = participantId,
                RawDamage = amount,
                Tags = DamageTags.DamageOverTime,
                Source = "持续伤害"
            });
        }

        // ---------------- 控制/状态 ----------------

        public bool ApplyStatus(CombatStatusSpec spec)
        {
            if (IsDead || spec == null)
            {
                return false;
            }

            combat.ApplyStatus(spec);
            return true;
        }

        /// <summary>眩晕；抗性窗口内的眩晕无效。返回是否真的晕住。</summary>
        public bool ApplyStun(float seconds, float resistanceSeconds = 1.5f)
        {
            if (seconds <= 0f || IsDead)
            {
                return false;
            }

            if (combatTime < stunResistanceUntil || IsControlImmune)
            {
                return false;
            }

            combat.ApplyStatus(CombatStatusSpec.Stun(seconds, 0));
            if (resistanceSeconds > 0f)
            {
                // 抗性窗口从眩晕结束才开始算：「眩晕后 1.5s 抗性」。
                stunResistanceUntil = combatTime + seconds + resistanceSeconds;
            }

            return true;
        }

        /// <summary>清除全部持续伤害，但保留减速/定身等其它负面（钟表师「回溯」）。</summary>
        public int CleanseDamageOverTime()
        {
            if (combat == null)
            {
                return 0;
            }

            List<string> ids = null;
            var all = combat.Status.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Kind == StatusKind.DamageOverTime)
                {
                    ids ??= new List<string>();
                    ids.Add(all[i].Id);
                }
            }

            if (ids == null)
            {
                return 0;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                combat.Status.Remove(ids[i]);
            }

            return ids.Count;
        }

        public bool ApplyRoot(float seconds)
        {
            if (seconds <= 0f || IsDead || IsControlImmune)
            {
                return false;
            }

            combat.ApplyStatus(CombatStatusSpec.Root(seconds, 0));
            return true;
        }

        public bool ApplySlow(float percent, float seconds)
        {
            if (percent <= 0f || seconds <= 0f || IsDead || IsControlImmune)
            {
                return false;
            }

            combat.ApplyStatus(CombatStatusSpec.Slow(percent, seconds, 0));
            return true;
        }

        public bool ApplyVulnerability(float percent, float seconds)
        {
            if (percent <= 0f || seconds <= 0f || IsDead)
            {
                return false;
            }

            combat.ApplyStatus(CombatStatusSpec.Vulnerability(percent, seconds, 0));
            return true;
        }

        public bool ApplyDamageOverTime(string id, float tickDamage, float tickInterval, float duration, int sourceId)
        {
            if (IsDead || tickDamage <= 0f || tickInterval <= 0f || duration <= 0f)
            {
                return false;
            }

            combat.ApplyStatus(CombatStatusSpec.DamageOverTime(id, tickDamage, tickInterval, duration, sourceId));
            return true;
        }

        public int CleanseDebuffs(int max = 0)
        {
            return combat.CleanseDebuffs(max);
        }

        public void AddShield(float amount, float seconds)
        {
            if (amount <= 0f || IsDead)
            {
                return;
            }

            combat.AddShield(amount, seconds);
        }

        public void GrantInvulnerability(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            invulnerableUntil = Mathf.Max(invulnerableUntil, combatTime + seconds);
        }

        public void GrantControlImmunity(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            controlImmuneUntil = Mathf.Max(controlImmuneUntil, combatTime + seconds);
        }

        // ---------------- 快照/回滚 ----------------

        public CharacterSnapshot CaptureSnapshot()
        {
            var snapshot = new CharacterSnapshot
            {
                Time = combatTime,
                Health = currentHealth,
                X = transform.position.x,
                Y = transform.position.y,
                Shield = combat.Shield.Amount,
                ShieldRemaining = combat.Shield.TimeRemaining,
                Statuses = combat.Status.Capture(),
                Stance = combat.Stance.Capture(),
                Modifiers = abilities != null ? abilities.CaptureModifiers() : Array.Empty<AbilityModifierState>(),
                Valid = true
            };

            snapshots.Push(snapshot);
            return snapshot;
        }

        /// <summary>按快照回滚。PhysiologicalOnly 只回生命/Buff/护盾，Full 连坐标一起回。</summary>
        public bool Rewind(float secondsAgo, StateRestoreMode mode, float maxHeal = 0f, bool noBackslide = false)
        {
            RewindPlan plan;
            if (!RewindPlanner.TryBuild(snapshots, combatTime, secondsAgo, mode, currentHealth, maxHealth,
                    maxHeal, noBackslide, out plan))
            {
                return false;
            }

            CharacterSnapshot snapshot = plan.Snapshot;
            currentHealth = Mathf.Clamp(plan.HealthToApply, 1f, maxHealth);
            deathRaised = false;
            if (Participant != null)
            {
                Participant.IsAlive = true;
            }

            combat.Status.Restore(snapshot.Statuses);
            combat.Stance.Restore(snapshot.Stance);
            combat.Shield.Clear();
            if (snapshot.Shield > 0f)
            {
                combat.Shield.Add(snapshot.Shield, Mathf.Max(0.05f, snapshot.ShieldRemaining));
            }

            if (abilities != null)
            {
                abilities.RestoreModifiers(snapshot.Modifiers);
                abilities.SetHealth(currentHealth, maxHealth);
            }

            if (plan.RestorePosition)
            {
                transform.position = new Vector3(snapshot.X, snapshot.Y, transform.position.z);
                CombatBody combatBody = Body;
                if (combatBody != null)
                {
                    combatBody.Teleport(new Vec2(snapshot.X, snapshot.Y));
                }
            }

            RaiseHealthChanged();
            CombatEvents.RaiseStateRestored(new StateRestoredEvent(participantId, mode, secondsAgo, currentHealth));
            return true;
        }

        /// <summary>自己造成一次伤害后调用（剑意叠层等）。</summary>
        public void NotifyDamageDealt()
        {
            if (abilities != null)
            {
                abilities.NotifyDamageDealt();
            }
        }

        /// <summary>
        /// 平A 命中一个目标：结算风之优雅 / 重箭、力量眩晕计数、过载损耗蓄势击退。
        /// 返回的 BonusDamage / KnockbackCells / Stunned 由战斗层消费。
        /// </summary>
        public BasicAttackHitResult NotifyBasicAttackHit(int targetId)
        {
            return abilities != null ? abilities.NotifyBasicAttackHit(targetId) : default(BasicAttackHitResult);
        }

        /// <summary>
        /// 钟声换牌之后把「手上持有的特质与技能卡」同步进五个能力槽。
        /// 普攻槽不受影响，会在同步里保留下来；血量/位置/Buff 不重置。
        /// </summary>
        public void RefreshLoadout()
        {
            if (loadout == null || Participant == null)
            {
                return;
            }

            List<AbilityCard> rejected;
            loadout.SyncFromBellCards(Participant.Traits, Participant.Skills, out rejected);

            if (rejected != null && rejected.Count > 0)
            {
                var builder = new System.Text.StringBuilder();
                for (int i = 0; i < rejected.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append('、');
                    }

                    builder.Append(rejected[i].ToDisplayName());
                }

                Debug.LogWarning("[" + DisplayName + "] 有 " + rejected.Count + " 张卡放不进能力槽：" + builder, this);
            }

            if (abilities != null)
            {
                // 热重载/特质可能改变最大生命：按原血量比例同步，避免出现当前血量大于上限。
                float nextMax = Mathf.Max(1f, abilities.MaxHealth);
                if (!Mathf.Approximately(nextMax, maxHealth))
                {
                    float ratio = maxHealth > 0f ? currentHealth / maxHealth : 1f;
                    maxHealth = nextMax;
                    currentHealth = Mathf.Clamp(ratio * maxHealth, 1f, maxHealth);
                }

                abilities.NotifyHealth(currentHealth);
            }

            // 特质可能刚被夺取/重新分配：形象跟着「特质所属人设」走，这里统一刷新。
            BuildVisual();
        }

        /// <summary>发放本命普攻卡（普攻不属于钟声卡池，生成时直接给）。</summary>
        public void GrantBasicAttack()
        {
            if (loadout == null)
            {
                return;
            }

            loadout.TryAdd(new AbilityCard(persona, AbilitySlot.BasicAttack), out _);
        }

        private void ApplyRestoredHealth(float restored)
        {
            currentHealth = Mathf.Clamp(restored, 1f, maxHealth);
            deathRaised = false;
            if (Participant != null)
            {
                Participant.IsAlive = true;
            }

            if (abilities != null)
            {
                abilities.NotifyHealth(currentHealth);
            }

            RaiseHealthChanged();
        }

        private void HealSilently(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            float next = Mathf.Min(maxHealth, currentHealth + amount);
            if (Mathf.Abs(next - currentHealth) < 0.01f)
            {
                currentHealth = next;
                return;
            }

            currentHealth = next;
            if (abilities != null)
            {
                abilities.NotifyHealth(currentHealth);
            }

            RaiseHealthChanged();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            float before = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            abilities?.NotifyHealth(currentHealth);
            RaiseHealthChanged();
            CombatEvents.RaiseHealed(new HealEvent(0, participantId, currentHealth - before));
        }

        /// <summary>直接判死（B11 回血、演示脚本、剧情处决都用这个）。</summary>
        public void Kill()
        {
            if (IsDead)
            {
                return;
            }

            currentHealth = 0f;
            RaiseHealthChanged();
            RaiseDied(0);
        }

        /// <summary>总表里没有指定人设时，把已经生成的角色提升为玩家（换色 + 改名）。</summary>
        public void PromoteToPlayer()
        {
            if (isPlayer)
            {
                return;
            }

            isPlayer = true;
            gameObject.name = "玩家_" + DisplayName;
            BuildVisual();
        }

        /// <summary>
        /// B8：普通阶段阵亡只是「被重新分配到某个区域并恢复满血」，不是淘汰。
        /// </summary>
        public void Revive()
        {
            currentHealth = maxHealth;
            deathRaised = false;
            if (Participant != null)
            {
                Participant.IsAlive = true;
                Participant.IsInPlay = true;
            }

            combat.Reset();
            snapshots.Clear();
            combatTime = 0f;
            invulnerableUntil = 0f;
            stunResistanceUntil = 0f;
            controlImmuneUntil = 0f;
            nextSnapshotTime = 0f;

            if (abilities != null)
            {
                // 满血重新分配：冷却、增益、叠层、计数与生命快照一起清掉。
                abilities.Reset(maxHealth, maxHealth);
            }

            RefreshLoadout();
            RaiseHealthChanged();
            CaptureSnapshot();
        }

        public void Despawn()
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }

        private void RaiseDied(int killerId)
        {
            if (deathRaised)
            {
                return;
            }

            deathRaised = true;
            if (Participant != null)
            {
                Participant.IsAlive = false;
            }

            CombatEvents.RaiseDied(new DeathEvent(participantId, killerId));
            if (Died != null)
            {
                Died(this);
            }
        }

        private void RaiseHealthChanged()
        {
            if (HealthChanged != null)
            {
                HealthChanged(this);
            }
        }

        // ---------- 视觉构建（形象跟随特质） ----------

        private void BuildVisual()
        {
            // 跳跃需要一个单独的父节点做纵向偏移：SwordsmanSpriteAnimation 会每帧覆写
            // Visual.localPosition 做脚底对齐，直接改 Visual 会被它顶掉。
            Transform jumpOffset = transform.Find("JumpOffset");
            if (jumpOffset == null)
            {
                var offsetGo = new GameObject("JumpOffset");
                offsetGo.transform.SetParent(transform, false);
                jumpOffset = offsetGo.transform;
            }

            Transform visual = jumpOffset.Find("Visual");
            GameObject go;
            if (visual == null)
            {
                go = new GameObject("Visual");
                go.transform.SetParent(jumpOffset, false);
            }
            else
            {
                go = visual.gameObject;
            }

            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = go.AddComponent<SpriteRenderer>();
            }

            renderer.sortingOrder = isPlayer ? 10 : 5;

            // 形象跟着「特质」走：特质属于什么人设，就用什么人设的序列帧。
            // 颜色、尺寸与脚底对齐全部交给动画组件接管，这里不能再套占位色块。
            PersonaId target = VisualPersona;

            if (visualBuilt && target == visualPersona)
            {
                // 人设没变（提升为玩家、换牌但特质未变）：让现有组件重新套一次状态。
                RefreshVisualAnimation(go, target);
                return;
            }

            // 人设变了：清掉旧的表现层（动画组件 + 它们自己生成的子物体），避免两套动画
            // 抢同一个 SpriteRenderer，也避免旧组件的残留子物体（如游侠的光晕）留在场上。
            ClearVisualExtras(go);
            renderer.sprite = null;
            renderer.color = Color.white;
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;

            // 新渲染器默认朝右；CombatBody 只在朝向真发生翻转时才写 flipX，这里补一次。
            CombatBody combatBody = Body;
            if (combatBody != null)
            {
                renderer.flipX = combatBody.FacingSign < 0;
            }

            visualPersona = target;
            visualBuilt = true;
            RefreshVisualAnimation(go, target);
        }

        /// <summary>给 Visual 挂上（或刷新）该人设的序列帧组件。</summary>
        private static void RefreshVisualAnimation(GameObject visual, PersonaId target)
        {
            switch (target)
            {
                case PersonaId.Spartan:
                {
                    var animation = visual.GetComponent<SpartanSpriteAnimation>();
                    if (animation == null)
                    {
                        // AddComponent 会立即触发 Awake，由组件自己加载帧并套用脚底对齐。
                        visual.AddComponent<SpartanSpriteAnimation>();
                    }
                    else
                    {
                        animation.RefreshVisual();
                    }

                    return;
                }

                case PersonaId.Strongman:
                {
                    var animation = visual.GetComponent<StrongmanSpriteAnimation>();
                    if (animation == null)
                    {
                        visual.AddComponent<StrongmanSpriteAnimation>();
                    }
                    else
                    {
                        animation.RefreshVisual();
                    }

                    return;
                }

                case PersonaId.Ranger:
                {
                    var animation = visual.GetComponent<ElfRangerSpriteAnimation>();
                    if (animation == null)
                    {
                        visual.AddComponent<ElfRangerSpriteAnimation>();
                    }
                    else
                    {
                        animation.RefreshVisual();
                    }

                    return;
                }

                default:
                {
                    var animation = visual.GetComponent<SwordsmanSpriteAnimation>();
                    if (animation == null)
                    {
                        visual.AddComponent<SwordsmanSpriteAnimation>();
                    }
                    else
                    {
                        animation.RefreshVisual();
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// 换人设时清掉 Visual 上的旧表现层：动画组件自己生成的表现子物体（如游侠的 Halo），
        /// 以及除 Transform / SpriteRenderer 之外的所有组件。
        /// 这里不逐个枚举动画组件类型——以后新人设自带新的表现组件时不用回头改这里。
        /// </summary>
        private static void ClearVisualExtras(GameObject visual)
        {
            for (int i = visual.transform.childCount - 1; i >= 0; i--)
            {
                DestroyVisualObject(visual.transform.GetChild(i).gameObject);
            }

            Component[] components = visual.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform || component is SpriteRenderer)
                {
                    continue;
                }

                if (component is Behaviour behaviour)
                {
                    // 先停掉，避免等 Destroy 真正生效前它又跑一帧 Update。
                    behaviour.enabled = false;
                }

                DestroyVisualObject(component);
            }
        }

        /// <summary>
        /// 运行期一律 Destroy：钟声可能从物理/触发回调里触发，那里不允许 DestroyImmediate；
        /// 编辑期（编辑器里预览构建形象）只能 DestroyImmediate。
        /// </summary>
        private static void DestroyVisualObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>六人设各一个辨识色，玩家额外加亮。</summary>
        public static Color GetPersonaColor(PersonaId persona, bool player)
        {
            Color color;
            switch (persona)
            {
                case PersonaId.Swordsman: color = new Color(0.42f, 0.60f, 0.86f); break;
                case PersonaId.SteamMechanic: color = new Color(0.85f, 0.55f, 0.25f); break;
                case PersonaId.Spartan: color = new Color(0.78f, 0.30f, 0.28f); break;
                case PersonaId.Ranger: color = new Color(0.40f, 0.72f, 0.44f); break;
                case PersonaId.Clockmaker: color = new Color(0.62f, 0.45f, 0.80f); break;
                case PersonaId.Strongman: color = new Color(0.80f, 0.72f, 0.35f); break;
                default: color = Color.gray; break;
            }

            return player ? Color.Lerp(color, Color.white, 0.35f) : color;
        }
    }
}
