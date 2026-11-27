using System.Collections;
using System.Collections.Generic;
using Ring.Abilities;
using Ring.Bells;
using Ring.Characters;
using Ring.Combat;
using Ring.Inputs;
using Ring.Maps;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 把按键接到五个能力槽，并用统一的格坐标命中判定结算：
    /// 矩形/扇形/圆即时判定、直线弹道、折返弹、冲刺、状态与姿态。
    /// 数值仍由 <see cref="AbilityRuntime"/> 派生，本类不自己改任何属性。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterInstance))]
    [AddComponentMenu("钟声/玩家技能 AbilityCaster")]
    public sealed class AbilityCaster : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("一格对应多少世界单位。当前战斗核心按 1 格 = 1 世界单位。")]
        private float unitsPerCell = 1f;

        [SerializeField]
        [Tooltip("是否允许鼠标左键打出一次平A。")]
        private bool enableBasicAttack = true;

        [SerializeField]
        [Tooltip("是否把施放与命中结果打印到 Console。")]
        private bool logCasts = true;

        [SerializeField]
        [Tooltip("施放超我时记录鼠标选点格，供 HUD / 地形技能读取。")]
        private bool logAimPointForSuperego = true;

        public GridCoord LastAimCell { get; private set; }
        public bool HasLastAimCell { get; private set; }

        private CharacterInstance instance;
        private CombatBody body;
        private readonly ProjectileWorld projectiles = new ProjectileWorld();
        private readonly List<CombatTargetInfo> targetBuffer = new List<CombatTargetInfo>(8);
        private readonly List<int> hitBuffer = new List<int>(8);

        private SwordsmanSpriteAnimation swordAnimation;
        private bool swordAnimationSearched;
        private float projectileTime;
        private float meditationHealTimer;
        private readonly Dictionary<ProjectileState, ProjectileView> projectileViews =
            new Dictionary<ProjectileState, ProjectileView>();

        public int ActiveProjectileCount => projectiles.Count;

        private void Awake()
        {
            instance = GetComponent<CharacterInstance>();
            body = GetComponent<CombatBody>();
        }

        private void Update()
        {
            if (instance == null)
            {
                return;
            }

            // 弹道属于世界，不随施法者死亡而冻结。
            if (projectiles.Count > 0)
            {
                projectileTime += Time.deltaTime;
                projectiles.Tick(Time.deltaTime, BuildTargetInfos(), HandleProjectileHit);
            }

            SyncProjectileViews();

            if (instance.IsDead)
            {
                return;
            }

            TickMeditation();

            GameInput input = GameInput.Instance;
            if (input == null)
            {
                return;
            }

            if (input.SkillPressed((int)SkillTier.Id))
            {
                Cast(AbilitySlot.Id);
            }

            if (input.SkillPressed((int)SkillTier.Ego))
            {
                Cast(AbilitySlot.Ego);
            }

            if (input.SkillPressed((int)SkillTier.Superego))
            {
                Cast(AbilitySlot.Superego);
            }

            if (enableBasicAttack && input.AttackPressed)
            {
                BasicAttack();
            }
        }

        /// <summary>施放一个技能槽；返回是否真的放出去了。</summary>
        public bool Cast(AbilitySlot slot)
        {
            if (instance == null || instance.Abilities == null || instance.IsDead)
            {
                return false;
            }

            AbilityDefinition definition = instance.Loadout != null ? instance.Loadout.GetDefinition(slot) : null;
            if (definition == null)
            {
                return false;
            }

            // 举盾/调息这类可提前解除的姿态：再按一次自我键直接收势。
            if (slot == AbilitySlot.Ego
                && instance.Combat.Stance.IsActive
                && instance.Combat.Stance.CurrentId == ResolveStanceId(definition, slot)
                && definition.CanCancelEarly)
            {
                instance.Combat.CancelStance();
                instance.Abilities.CancelModifiers(slot);
                if (logCasts)
                {
                    Debug.Log("[技能] 提前解除 " + definition.ToDisplayName(), this);
                }

                return true;
            }

            if (slot != AbilitySlot.BasicAttack && !instance.Combat.CanCastSkill(slot))
            {
                if (logCasts)
                {
                    Debug.Log("[技能] 姿态中无法施放 " + definition.ToDisplayName(), this);
                }

                return false;
            }

            AbilityActivation activation = instance.Abilities.TryActivate(slot);
            if (!activation.Success)
            {
                if (logCasts)
                {
                    Debug.Log("[技能] " + activation.FailureReason, this);
                }

                return false;
            }

            // 钟表师「回溯」：回复上限 25 由快照回滚统一结算，不能再叠加一次通用治疗。
            bool isClockmakerRewind = definition.Persona == PersonaId.Clockmaker
                                      && slot == AbilitySlot.Ego;
            if (!isClockmakerRewind && activation.HealAmount > 0f)
            {
                instance.Heal(activation.HealAmount);
            }

            if (isClockmakerRewind)
            {
                if (!instance.Rewind(definition.DurationSeconds, StateRestoreMode.PhysiologicalOnly,
                        activation.HealAmount, true))
                {
                    instance.Heal(activation.HealAmount);
                }

                instance.CleanseDamageOverTime();
            }

            // 调息的护盾只在「全程未挨打」结束时给，施法瞬间不能先领一份。
            bool shieldOnStanceEnd = definition.Persona == PersonaId.Strongman
                                     && slot == AbilitySlot.Ego;
            if (!shieldOnStanceEnd && activation.ShieldAmount > 0f)
            {
                instance.AddShield(activation.ShieldAmount, activation.ShieldSeconds);
            }

            if (definition.EffectRefs != null)
            {
                for (int i = 0; i < definition.EffectRefs.Count; i++)
                {
                    if (definition.EffectRefs[i].EffectId == AbilityEffectId.Cleanse)
                    {
                        instance.CleanseDebuffs();
                    }
                }
            }

            ApplyStance(definition, slot);
            ExecuteAbility(activation, slot);

            if (slot == AbilitySlot.Superego)
            {
                RecordAimPoint();
            }

            Vec2 aim = GetAimDirection();
            CombatEvents.RaiseSkillCast(new SkillCastEvent(instance.Id, slot, definition.ToDisplayName(), aim, HasAim()));
            if (logCasts)
            {
                Debug.Log("[技能] 施放 " + definition.ToDisplayName()
                    + "（冷却 " + AbilityMath.Number(activation.CooldownSeconds) + "s）", this);
            }

            return true;
        }

        private static string ResolveStanceId(AbilityDefinition definition, AbilitySlot slot)
        {
            if (definition.Persona == PersonaId.Spartan && slot == AbilitySlot.Ego)
            {
                return CombatStances.ShieldWall;
            }

            if (definition.Persona == PersonaId.Spartan && slot == AbilitySlot.Superego)
            {
                return CombatStances.Berserk;
            }

            if (definition.Persona == PersonaId.Strongman && slot == AbilitySlot.Ego)
            {
                return CombatStances.Meditation;
            }

            return null;
        }

        private void ApplyStance(AbilityDefinition definition, AbilitySlot slot)
        {
            string stanceId = ResolveStanceId(definition, slot);
            if (stanceId == null)
            {
                return;
            }

            if (!instance.Combat.BeginStance(stanceId))
            {
                return;
            }

            // 举盾/调息的移速与受伤倍率由姿态表统一提供，撤掉技能定义里重复注册的同类修正。
            // 狂战还需要定义里的防御/攻击间隔/普攻伤害修正，因此不能整槽清空。
            if (stanceId == CombatStances.ShieldWall || stanceId == CombatStances.Meditation)
            {
                instance.Abilities.CancelModifiers(slot);
            }

            meditationHealTimer = 0f;
        }

        private void ExecuteAbility(AbilityActivation activation, AbilitySlot slot)
        {
            AbilityDefinition definition = activation.Definition;
            if (definition == null || definition.Shape == AbilityShape.Self || definition.Shape == AbilityShape.None)
            {
                return;
            }

            if (slot == AbilitySlot.Id && definition.DashCells > 0f)
            {
                ExecuteDash(definition);
                return;
            }

            if (definition.Persona == PersonaId.Strongman && slot == AbilitySlot.Superego)
            {
                ExecuteAnchor(definition);
                return;
            }

            if (definition.ProjectileSpeed > 0f &&
                (definition.Shape == AbilityShape.Line || definition.Shape == AbilityShape.Fan))
            {
                SpawnProjectiles(definition, slot);
                return;
            }

            ResolveShape(definition, GetAimDirection(), definition.Damage, DamageTags.Skill,
                definition.KnockbackCells, definition.SlowPercent, definition.SlowSeconds,
                definition.StunSeconds, definition.RootSeconds, definition.DamageTakenBonusPercent,
                EffectDuration(definition));
        }

        // ---------------- 弹道 ----------------

        private void SpawnProjectiles(AbilityDefinition definition, AbilitySlot slot)
        {
            Vec2 origin = ToVec2(transform.position);
            Vec2 direction = GetAimDirection();
            float width = definition.AreaWidthCells > 0f ? definition.AreaWidthCells : 0.5f;
            float distance = definition.RangeCells > 0f ? definition.RangeCells : Mathf.Max(1f, definition.AreaLengthCells);
            int tags = (slot == AbilitySlot.BasicAttack ? DamageTags.BasicAttack : DamageTags.Skill)
                | DamageTags.Projectile;

            if (definition.Shape == AbilityShape.Fan && definition.PelletCount > 1)
            {
                var template = new ProjectileSpec
                {
                    OwnerId = instance.Id,
                    OpponentFilter = PairedOpponentId(),
                    Origin = origin,
                    Direction = direction,
                    Speed = Mathf.Max(0.1f, definition.ProjectileSpeed),
                    MaxDistance = distance,
                    Width = Mathf.Max(0.2f, width),
                    Damage = definition.PelletDamage > 0f ? definition.PelletDamage : definition.Damage,
                    Lifetime = 4f,
                    Returns = false,
                    StopOnFirst = true,
                    Tags = tags,
                    KnockbackCells = definition.KnockbackCells,
                    SlowPercent = definition.SlowPercent,
                    SlowSeconds = definition.SlowSeconds,
                    StunSeconds = definition.StunSeconds,
                    RootSeconds = definition.RootSeconds,
                    VulnerabilityPercent = definition.DamageTakenBonusPercent,
                    VulnerabilitySeconds = EffectDuration(definition),
                    Slot = (int)slot
                };
                projectiles.SpawnFan(template, definition.PelletCount, definition.SpreadDegrees);
                return;
            }

            projectiles.Spawn(new ProjectileSpec
            {
                OwnerId = instance.Id,
                OpponentFilter = PairedOpponentId(),
                Origin = origin,
                Direction = direction,
                Speed = Mathf.Max(0.1f, definition.ProjectileSpeed),
                MaxDistance = distance,
                Width = Mathf.Max(0.2f, width),
                Damage = definition.Damage,
                SecondaryDamage = definition.SecondaryDamage,
                Lifetime = 4f,
                Returns = definition.Returns,
                StopOnFirst = true,
                Tags = tags,
                KnockbackCells = definition.KnockbackCells,
                SlowPercent = definition.SlowPercent,
                SlowSeconds = definition.SlowSeconds,
                StunSeconds = definition.StunSeconds,
                RootSeconds = definition.RootSeconds,
                VulnerabilityPercent = definition.DamageTakenBonusPercent,
                VulnerabilitySeconds = EffectDuration(definition),
                Slot = (int)slot
            });
        }

        private void HandleProjectileHit(ProjectileState projectile, CombatTargetInfo target, Vec2 point, float damage)
        {
            CharacterInstance victim = FindInstance(target.Id);
            if (victim == null || victim.IsDead)
            {
                return;
            }

            float finalDamage = damage;
            BasicAttackHitResult basicHit = default;
            if (projectile.Spec.Slot == (int)AbilitySlot.BasicAttack)
            {
                basicHit = instance.NotifyBasicAttackHit(victim.Id);
                finalDamage += basicHit.BonusDamage;
            }

            DamageResult result = victim.TakeDamage(new DamageRequest
            {
                AttackerId = instance.Id,
                OutgoingMultiplier = instance.Derived.DamageDealtMultiplier,
                TargetId = victim.Id,
                RawDamage = finalDamage,
                Tags = projectile.Spec.Tags,
                Direction = projectile.Spec.Direction,
                HitPoint = point,
                KnockbackCells = projectile.Spec.KnockbackCells + basicHit.KnockbackCells,
                Source = "弹道"
            });

            if (HitLanded(result))
            {
                ApplyStatuses(victim, projectile.Spec.BuildStatuses());
                if (projectile.Spec.Slot == (int)AbilitySlot.BasicAttack && basicHit.Stunned)
                {
                    victim.ApplyStun(basicHit.StunSeconds, basicHit.StunResistanceSeconds);
                }

                if (!result.Killed)
                {
                    instance.NotifyDamageDealt();
                }
            }
        }

        // ---------------- 即时形状 ----------------

        private void ResolveShape(AbilityDefinition definition, Vec2 direction, float damage, int tags,
            float knockback, float slowPercent, float slowSeconds, float stunSeconds, float rootSeconds,
            float vulnerabilityPercent, float effectDuration)
        {
            HitShape shape = HitShape.FromDefinition(definition);
            if (shape == null)
            {
                return;
            }

            BuildTargetInfos();
            hitBuffer.Clear();
            shape.Query(ToVec2(transform.position), direction, targetBuffer, hitBuffer, instance.Id);

            for (int i = 0; i < hitBuffer.Count; i++)
            {
                CharacterInstance victim = FindInstance(hitBuffer[i]);
                if (victim == null || victim.IsDead)
                {
                    continue;
                }

                Vec2 victimPosition = ToVec2(victim.transform.position);
                DamageResult result = victim.TakeDamage(new DamageRequest
                {
                    AttackerId = instance.Id,
                    OutgoingMultiplier = instance.Derived.DamageDealtMultiplier,
                    TargetId = victim.Id,
                    RawDamage = damage,
                    Tags = tags,
                    Direction = (victimPosition - ToVec2(transform.position)).Normalized,
                    HitPoint = victimPosition,
                    KnockbackCells = knockback,
                    Source = definition.AbilityName
                });

                if (HitLanded(result))
                {
                    if (slowPercent > 0f && slowSeconds > 0f)
                    {
                        victim.ApplySlow(slowPercent, slowSeconds);
                    }

                    if (stunSeconds > 0f)
                    {
                        victim.ApplyStun(stunSeconds, definition.StunResistanceSeconds > 0f
                            ? definition.StunResistanceSeconds
                            : 1.5f);
                    }

                    if (rootSeconds > 0f)
                    {
                        victim.ApplyRoot(rootSeconds);
                    }

                    if (vulnerabilityPercent > 0f && effectDuration > 0f)
                    {
                        victim.ApplyVulnerability(vulnerabilityPercent, effectDuration);
                    }

                    ApplyAreaActionSpeed(definition, victim, effectDuration);

                    if (!result.Killed)
                    {
                        instance.NotifyDamageDealt();
                    }
                }
            }
        }

        private void ApplyStatuses(CharacterInstance victim, CombatStatusSpec[] statuses)
        {
            if (statuses == null)
            {
                return;
            }

            for (int i = 0; i < statuses.Length; i++)
            {
                victim.ApplyStatus(statuses[i]);
            }
        }

        /// <summary>
        /// 范围技能的目标侧动作速度修正（钟表师「慢摆」：移速/攻速 ×0.6）。
        /// 定义为倍率，状态里存增量，故 0.6 → Value = -0.4。
        /// </summary>
        private void ApplyAreaActionSpeed(AbilityDefinition definition, CharacterInstance victim, float duration)
        {
            if (victim == null || duration <= 0f)
            {
                return;
            }

            ApplyActionSpeedStatus(victim, "slowfield.move", "慢摆·移速",
                StatusKind.MoveSpeed, definition.MoveSpeedMultiplier, duration);
            ApplyActionSpeedStatus(victim, "slowfield.attack", "慢摆·攻速",
                StatusKind.AttackSpeed, definition.AttackSpeedMultiplier, duration);
        }

        private void ApplyActionSpeedStatus(CharacterInstance victim, string id, string displayName,
            StatusKind kind, float multiplier, float duration)
        {
            if (multiplier <= 0f || CombatMath.Approximately(multiplier, 1f))
            {
                return;
            }

            victim.ApplyStatus(new CombatStatusSpec
            {
                Id = id,
                DisplayName = displayName,
                Kind = kind,
                Value = multiplier - 1f,
                Duration = duration,
                SourceId = instance.Id,
                IsDebuff = multiplier < 1f
            });
        }

        private float EffectDuration(AbilityDefinition definition)
        {
            if (definition.DurationSeconds > 0f)
            {
                return definition.DurationSeconds;
            }

            return Mathf.Max(0.5f, definition.WindupSeconds + 0.5f);
        }

        // ---------------- 突进 ----------------

        private void ExecuteDash(AbilityDefinition definition)
        {
            Vec2 direction = GetAimDirection();
            Vec2 start = ToVec2(transform.position);
            float distance = Mathf.Max(0f, definition.DashCells);
            float dashSeconds = definition.DashSpeedCellsPerSecond > 0.01f
                ? distance / definition.DashSpeedCellsPerSecond
                : 0.3f;
            instance.GrantControlImmunity(dashSeconds + 0.1f);
            Vec2 end = body != null ? body.MoveInstant(direction, distance) : start + direction * distance;
            float travelled = Vec2.Distance(start, end);

            System.Action resolve = () => ResolveDashHit(definition, start, direction, travelled);
            SwordsmanSpriteAnimation animation = SwordAnimation;
            if (definition.Persona == PersonaId.Swordsman && animation != null && animation.PlayDash(resolve))
            {
                return;
            }

            StrongmanSpriteAnimation strongman = GetComponentInChildren<StrongmanSpriteAnimation>();
            if (definition.Persona == PersonaId.Strongman && strongman != null && strongman.PlayDash(resolve))
            {
                return;
            }

            resolve();
        }

        private void ResolveDashHit(AbilityDefinition definition, Vec2 start, Vec2 direction, float travelled)
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            HitShape shape = HitShape.Rect(Mathf.Max(0.5f, travelled + definition.AreaLengthCells),
                Mathf.Max(0.5f, definition.AreaWidthCells));
            BuildTargetInfos();
            hitBuffer.Clear();
            Vec2 center = start + direction * (travelled * 0.5f);
            shape.Query(center, direction, targetBuffer, hitBuffer, instance.Id);
            bool anyHit = false;
            if (definition.Persona == PersonaId.Strongman && hitBuffer.Count > 1)
            {
                int nearest = shape.QueryFirst(center, direction, targetBuffer, instance.Id);
                hitBuffer.Clear();
                if (nearest >= 0)
                {
                    hitBuffer.Add(nearest);
                }
            }

            for (int i = 0; i < hitBuffer.Count; i++)
            {
                CharacterInstance victim = FindInstance(hitBuffer[i]);
                if (victim == null || victim.IsDead)
                {
                    continue;
                }

                anyHit = true;
                Vec2 victimPosition = ToVec2(victim.transform.position);
                float hitDistance = Vec2.Dot(victimPosition - start, direction);
                float damage = definition.Damage;
                float knockback = definition.KnockbackCells;
                float stun = definition.StunSeconds;
                if (definition.Persona == PersonaId.Strongman)
                {
                    ResolveStrongmanDashTier(hitDistance, out damage, out knockback, out stun);
                }

                DamageResult result = victim.TakeDamage(new DamageRequest
                {
                    AttackerId = instance.Id,
                    OutgoingMultiplier = instance.Derived.DamageDealtMultiplier,
                    TargetId = victim.Id,
                    RawDamage = damage,
                    Tags = DamageTags.Skill,
                    Direction = direction,
                    HitPoint = victimPosition,
                    KnockbackCells = knockback,
                    Source = definition.AbilityName
                });

                if (HitLanded(result) && !result.Killed)
                {
                    instance.NotifyDamageDealt();
                }

                if (HitLanded(result) && stun > 0f)
                {
                    victim.ApplyStun(stun, definition.StunResistanceSeconds);
                }

                if (definition.Persona == PersonaId.Strongman)
                {
                    // 崩进冲撞：命中首个目标即停。
                    break;
                }
            }

            if (definition.Persona == PersonaId.Strongman && !anyHit)
            {
                // 未命中：0.3s 收势硬直（用不可行动状态表达）。
                instance.ApplyStun(0.3f, 0f);
            }
        }

        /// <summary>力士崩进冲撞的分段表：1/2/3/4/5 格对应不同伤害、击退与眩晕。</summary>
        private static void ResolveStrongmanDashTier(float distance, out float damage, out float knockback,
            out float stun)
        {
            if (distance <= 1f)
            {
                damage = 4f;
                knockback = 1f;
                stun = 0f;
            }
            else if (distance <= 2f)
            {
                damage = 5f;
                knockback = 1.5f;
                stun = 0f;
            }
            else if (distance <= 3f)
            {
                damage = 6f;
                knockback = 2f;
                stun = 0f;
            }
            else if (distance <= 4f)
            {
                damage = 8f;
                knockback = 0.5f;
                stun = 0.6f;
            }
            else
            {
                damage = 10f;
                knockback = 0f;
                stun = 1f;
            }
        }

        // ---------------- 虚手锚定 ----------------

        private void ExecuteAnchor(AbilityDefinition definition)
        {
            Vec2 origin = ToVec2(transform.position);
            Vec2 direction = GetAimDirection();
            float width = Mathf.Max(0.5f, definition.AreaWidthCells);
            float length = Mathf.Max(1f, definition.AreaLengthCells > 0f
                ? definition.AreaLengthCells
                : definition.RangeCells);
            length = ClampLineToWalls(origin, direction, length);

            HitShape shape = HitShape.Line(length, width);
            BuildTargetInfos();
            int targetId = shape.QueryFirst(origin, direction, targetBuffer, instance.Id);
            if (targetId < 0)
            {
                if (logCasts)
                {
                    Debug.Log("[技能] 虚手锚定未命中", this);
                }

                return;
            }

            CharacterInstance victim = FindInstance(targetId);
            if (victim == null || victim.IsDead)
            {
                return;
            }

            Vec2 victimPosition = ToVec2(victim.transform.position);
            DamageResult result = victim.TakeDamage(new DamageRequest
            {
                AttackerId = instance.Id,
                OutgoingMultiplier = instance.Derived.DamageDealtMultiplier,
                TargetId = victim.Id,
                RawDamage = definition.Damage,
                Tags = DamageTags.Skill,
                Direction = direction,
                HitPoint = victimPosition,
                Source = definition.AbilityName
            });

            if (!HitLanded(result))
            {
                return;
            }

            if (!result.Killed)
            {
                instance.NotifyDamageDealt();
            }

            StartCoroutine(AnchorPull(victim, 1f, Mathf.Max(0.1f, definition.RootSeconds)));
        }

        private IEnumerator AnchorPull(CharacterInstance victim, float delay, float rootSeconds)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (victim == null || victim.IsDead)
            {
                yield break;
            }

            CombatBody targetBody = victim.Body;
            if (targetBody != null)
            {
                targetBody.PullTo(ToVec2(transform.position), 10f, 1.2f);
                float timeout = 1.2f;
                while (timeout > 0f && targetBody != null && targetBody.IsPulling)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            if (victim != null && !victim.IsDead)
            {
                victim.ApplyRoot(rootSeconds);
            }
        }

        private float ClampLineToWalls(Vec2 origin, Vec2 direction, float maxLength)
        {
            if (body == null || body.Walls == null || body.Walls.Count == 0)
            {
                return maxLength;
            }

            const float step = 0.2f;
            Vec2 previous = origin;
            for (float distance = step; distance <= maxLength + 0.001f; distance += step)
            {
                Vec2 next = origin + direction * distance;
                if (MotionSolver.Blocked(previous, next, 0.2f, body.Walls))
                {
                    return Mathf.Max(0f, distance - step);
                }

                previous = next;
            }

            return maxLength;
        }

        // ---------------- 平A ----------------

        public bool BasicAttack()
        {
            if (instance == null || instance.Abilities == null || instance.IsDead)
            {
                return false;
            }

            if (!instance.Combat.CanBasicAttack)
            {
                return false;
            }

            AbilityActivation activation = instance.Abilities.TryActivate(AbilitySlot.BasicAttack);
            if (!activation.Success)
            {
                return false;
            }

            AbilityDefinition definition = activation.Definition;
            if (definition == null)
            {
                return false;
            }

            System.Action resolve = () => ResolveBasicAttack(definition);

            SwordsmanSpriteAnimation animation = SwordAnimation;
            if (animation != null && animation.PlayAttack(resolve))
            {
                return true;
            }

            StrongmanSpriteAnimation strongman = GetComponentInChildren<StrongmanSpriteAnimation>();
            if (strongman != null && strongman.PlayPunch(resolve))
            {
                return true;
            }

            resolve();
            return true;
        }

        private void ResolveBasicAttack(AbilityDefinition definition)
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            if (definition.ProjectileSpeed > 0f &&
                (definition.Shape == AbilityShape.Line || definition.Shape == AbilityShape.Fan))
            {
                SpawnProjectiles(definition, AbilitySlot.BasicAttack);
                return;
            }

            DerivedAbilityStats derived = instance.Derived;
            CharacterInstance target = FindNearestTarget(Mathf.Max(0.5f, derived.AttackRangeCells));
            if (target == null)
            {
                if (logCasts)
                {
                    Debug.Log("[技能] 平A 挥空（" + AbilityMath.Number(derived.AttackRangeCells) + " 格内没有人）", this);
                }

                return;
            }

            BasicAttackHitResult hit = instance.NotifyBasicAttackHit(target.Id);
            float damage = derived.BasicAttackDamage + hit.BonusDamage;
            Vec2 direction = (ToVec2(target.transform.position) - ToVec2(transform.position)).Normalized;
            DamageResult result = target.TakeDamage(new DamageRequest
            {
                AttackerId = instance.Id,
                OutgoingMultiplier = instance.Derived.DamageDealtMultiplier,
                TargetId = target.Id,
                RawDamage = damage,
                Tags = DamageTags.BasicAttack,
                Direction = direction,
                KnockbackCells = hit.KnockbackCells,
                Source = definition.AbilityName
            });

            if (HitLanded(result))
            {
                instance.NotifyDamageDealt();
                if (hit.Stunned)
                {
                    target.ApplyStun(hit.StunSeconds, hit.StunResistanceSeconds);
                }
            }

            if (logCasts)
            {
                Debug.Log("[技能] 平A 命中 " + target.DisplayName + "，伤害 " + AbilityMath.Number(damage)
                    + "（" + hit + "）", this);
            }
        }

        // ---------------- 工具 ----------------

        private List<CombatTargetInfo> BuildTargetInfos()
        {
            targetBuffer.Clear();
            ArenaPlan plan = GameFlow.Instance != null ? GameFlow.Instance.CurrentPlan : null;
            int paired = PairedOpponentId();
            CharacterInstance[] all = FindObjectsOfType<CharacterInstance>();
            for (int i = 0; i < all.Length; i++)
            {
                CharacterInstance candidate = all[i];
                if (candidate == null || candidate == instance || candidate.IsDead)
                {
                    continue;
                }

                if (candidate.Participant != null && !candidate.Participant.IsInPlay)
                {
                    continue;
                }

                if (paired >= 0 && candidate.Id != paired)
                {
                    continue;
                }

                targetBuffer.Add(new CombatTargetInfo
                {
                    Id = candidate.Id,
                    OpponentId = paired,
                    Position = ToVec2(candidate.transform.position),
                    Radius = 0.3f,
                    IsAlive = !candidate.IsDead,
                    IsInPlay = candidate.Participant == null || candidate.Participant.IsInPlay
                });
            }

            return targetBuffer;
        }

        private int PairedOpponentId()
        {
            ArenaPlan plan = GameFlow.Instance != null ? GameFlow.Instance.CurrentPlan : null;
            return plan != null && plan.Contains(instance.Id) ? plan.OpponentOf(instance.Id) : -1;
        }

        private CharacterInstance FindInstance(int id)
        {
            CharacterInstance[] all = FindObjectsOfType<CharacterInstance>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == id)
                {
                    return all[i];
                }
            }

            return null;
        }

        private CharacterInstance FindNearestTarget(float rangeWorld)
        {
            CharacterInstance[] all = FindObjectsOfType<CharacterInstance>();
            CharacterInstance best = null;
            float bestDistance = float.MaxValue;
            int paired = PairedOpponentId();

            for (int i = 0; i < all.Length; i++)
            {
                CharacterInstance candidate = all[i];
                if (candidate == null || candidate == instance || candidate.IsDead)
                {
                    continue;
                }

                if (candidate.Participant != null && !candidate.Participant.IsInPlay)
                {
                    continue;
                }

                if (paired >= 0 && candidate.Id != paired)
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance <= rangeWorld && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private Vec2 GetAimDirection()
        {
            Vec2 origin = ToVec2(transform.position);
            GameInput input = GameInput.Instance;
            if (input != null && input.HasValidAim)
            {
                Vec2 aim = ToVec2(input.AimWorldPoint) - origin;
                if (aim.SqrMagnitude > 0.001f)
                {
                    return aim.Normalized;
                }
            }

            return body != null ? body.FacingVector : Vec2.Right;
        }

        private bool HasAim()
        {
            GameInput input = GameInput.Instance;
            return input != null && input.HasValidAim;
        }

        private void RecordAimPoint()
        {
            if (!logAimPointForSuperego)
            {
                return;
            }

            GameInput input = GameInput.Instance;
            if (input != null && input.HasValidAim)
            {
                LastAimCell = input.AimCell;
                HasLastAimCell = true;
            }
        }

        private void TickMeditation()
        {
            if (instance.Combat.Stance.CurrentId != CombatStances.Meditation)
            {
                meditationHealTimer = 0f;
                return;
            }

            meditationHealTimer += Time.deltaTime;
            while (meditationHealTimer >= 1f)
            {
                meditationHealTimer -= 1f;
                instance.Heal(instance.Combat.Stance.WasHit ? 2f : 4f);
            }
        }

        private void SyncProjectileViews()
        {
            IReadOnlyList<ProjectileState> all = projectiles.All;
            for (int i = 0; i < all.Count; i++)
            {
                ProjectileState state = all[i];
                ProjectileView view;
                if (!projectileViews.TryGetValue(state, out view) || view == null)
                {
                    view = ProjectileView.Create(transform, ProjectileColor(state.Spec), state.Spec.VisualSize);
                    projectileViews[state] = view;
                }

                view.SetWorldPosition(new Vector3(state.Position.X, state.Position.Y, 0f));
                view.SetReturning(state.Returning);
            }

            List<ProjectileState> stale = null;
            foreach (KeyValuePair<ProjectileState, ProjectileView> pair in projectileViews)
            {
                bool alive = false;
                for (int i = 0; i < all.Count; i++)
                {
                    if (ReferenceEquals(all[i], pair.Key))
                    {
                        alive = true;
                        break;
                    }
                }

                if (alive)
                {
                    continue;
                }

                if (stale == null)
                {
                    stale = new List<ProjectileState>();
                }

                stale.Add(pair.Key);
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    projectileViews.Remove(stale[i]);
                }
            }
        }

        private static Color ProjectileColor(ProjectileSpec spec)
        {
            if (DamageTags.Has(spec.Tags, DamageTags.BasicAttack))
            {
                return new Color(1f, 0.93f, 0.55f, 1f);
            }

            switch (spec.Slot)
            {
                case (int)AbilitySlot.Id:
                    return new Color(0.45f, 0.9f, 1f, 1f);
                case (int)AbilitySlot.Ego:
                    return new Color(0.55f, 1f, 0.6f, 1f);
                case (int)AbilitySlot.Superego:
                    return new Color(1f, 0.55f, 0.9f, 1f);
                default:
                    return Color.white;
            }
        }

        /// <summary>命中是否落地：即使伤害被护盾完全吸收，也算命中（用于叠层、控制与特质）。</summary>
        private static bool HitLanded(DamageResult result)
        {
            return result != null && (!result.Blocked || result.ShieldAbsorbed > 0f);
        }

        private static Vec2 ToVec2(Vector2 value) => new Vec2(value.x, value.y);
        private static Vec2 ToVec2(Vector3 value) => new Vec2(value.x, value.y);

        private SwordsmanSpriteAnimation SwordAnimation
        {
            get
            {
                if (!swordAnimationSearched)
                {
                    swordAnimationSearched = true;
                    swordAnimation = GetComponentInChildren<SwordsmanSpriteAnimation>();
                }

                return swordAnimation;
            }
        }
    }
}
