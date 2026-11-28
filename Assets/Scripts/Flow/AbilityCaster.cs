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

        /// <summary>
        /// 一格对应的世界单位。地图按 25×12 铺满视口时由落位层写入（约 0.83）；
        /// 射程、命中形状、弹道距离都吃这个值，必须和 CombatBody 保持一致。
        /// </summary>
        public void SetUnitsPerCell(float units)
        {
            unitsPerCell = units > 0f ? units : 1f;
        }

        private CharacterInstance instance;
        private CombatBody body;
        private readonly ProjectileWorld projectiles = new ProjectileWorld();
        private readonly List<CombatTargetInfo> targetBuffer = new List<CombatTargetInfo>(8);
        private readonly List<int> hitBuffer = new List<int>(8);

        private SwordsmanSpriteAnimation swordAnimation;
        private StrongmanSpriteAnimation strongmanAnimation;
        private SpartanSpriteAnimation spartanAnimation;
        private ElfRangerSpriteAnimation rangerAnimation;
        private Sprite[] arrowFrames;
        private bool arrowFramesLoaded;
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
            // AbilityCaster 也可能被场景里的旧对象或 AI 挂载；全局玩家输入只由玩家消费。
            // 非玩家仍会在上方推进自己已有的弹道与持续效果。
            if (input == null || !instance.IsPlayer)
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

        // ---------------- UI 按钮入口 ----------------
        // 想让「点击技能卡」也能施放：在 Inspector 里给按钮的 OnClick 拖本组件，
        // 选下面三个无参方法即可（不需要写任何额外的 UI 代码）。

        /// <summary>本我技能（1 号槽）。</summary>
        public void CastIdSkill()
        {
            Cast(AbilitySlot.Id);
        }

        /// <summary>自我技能（2 号槽）。</summary>
        public void CastEgoSkill()
        {
            Cast(AbilitySlot.Ego);
        }

        /// <summary>超我技能（3 号槽）。</summary>
        public void CastSuperegoSkill()
        {
            Cast(AbilitySlot.Superego);
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
                // 这里以前是静默 return：槽位空时按键完全没反馈，最难排查的一类「技能没反应」。
                if (logCasts)
                {
                    Debug.LogWarning("[技能] " + slot.ToDisplayName() + " 槽没有技能卡（Loadout 为空），"
                        + "按键 " + (int)slot.ToSkillTier() + " 不会生效。", this);
                }

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
                // 提前解除自我：让斯巴达立刻收盾，而不是等 2s 计时走完。
                SpartanAnimation?.EndShield();
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

            // 斯巴达表现：本我横扫在 ExecuteAbility 内以「命中帧回调」播放（见 TryPlayCasterAction），
            // 这里只补 ExecuteAbility 不会走到动画的两个槽位——自我举盾、超我投矛。
            // 判断依据用姿态 id 而不是人设：拆卡混装时施法者身上的姿态才是准的。
            SpartanSpriteAnimation spartan = SpartanAnimation;
            if (spartan != null && ResolveStanceId(definition, slot) == CombatStances.ShieldWall)
            {
                // 抬盾 0.5s → 保持到姿态结束（DurationSeconds 默认 2s）→ 倒序收盾。
                // 再次点击提前解除由上方 cancel 分支调用 EndShield()。
                spartan.BeginShield(definition.DurationSeconds);
            }
            else if (spartan != null && slot == AbilitySlot.Superego)
            {
                // 投矛复用突刺分镜：伤害由弹道自身结算，这里只做动作表现。
                spartan.PlayThrust(null);
            }

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
            if (definition == null)
            {
                return;
            }

            if (definition.Shape == AbilityShape.Self || definition.Shape == AbilityShape.None)
            {
                // 游侠「自然恩赐」：治疗由 activation 在施法瞬间结算，这里只补表现。
                // 光晕（减伤 1.5s）与治疗分镜一起点亮，玩家才看得出自己进了减伤状态。
                if (slot == AbilitySlot.Ego && definition.HealAmount > 0f)
                {
                    RangerAnimation?.PlayHeal(definition.DurationSeconds);
                }

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

            // 游侠「缚地荆棘」：鼠标选点生成地形，本身不造成伤害，
            // 由 ElfRangerBrambleZone 负责长出 / 保持 / 消散与「离开即缠绕」。
            if (definition.Shape == AbilityShape.Ground)
            {
                SpawnBrambleZone(definition);
                return;
            }

            if (definition.ProjectileSpeed > 0f &&
                (definition.Shape == AbilityShape.Line || definition.Shape == AbilityShape.Fan))
            {
                // 游侠的箭要等「撒放帧」才离弦：破甲重箭用重箭分镜，其余弹道用短弓分镜。
                ElfRangerSpriteAnimation ranger = RangerAnimation;
                if (ranger != null)
                {
                    System.Action release = () => SpawnProjectiles(definition, slot);
                    bool heavyShot = slot == AbilitySlot.Id;
                    if (heavyShot ? ranger.PlayHeavy(release) : ranger.PlayAttack(release))
                    {
                        return;
                    }
                }

                SpawnProjectiles(definition, slot);
                return;
            }

            // 朝向在「按下瞬间」锁定：结算要等动画命中帧，届时鼠标可能已经移开了。
            Vec2 aimedDirection = GetAimDirection();
            System.Action resolve = () => ResolveShape(definition, aimedDirection, definition.Damage, DamageTags.Skill,
                definition.KnockbackCells, definition.SlowPercent, definition.SlowSeconds,
                definition.StunSeconds, definition.RootSeconds, definition.DamageTakenBonusPercent,
                EffectDuration(definition));

            // 与平A一致：非位移、非弹道的技能也要有挥砍/出拳表现，
            // 否则伤害在第 0 帧就结算，画面上人物纹丝不动 —— 玩家会当成「技能没生效」。
            if (TryPlayCasterAction(resolve))
            {
                return;
            }

            resolve();
        }

        // ---------------- 弹道 ----------------

        private void SpawnProjectiles(AbilityDefinition definition, AbilitySlot slot)
        {
            // 防御性兜底：普攻任何情况下都不允许生成飞行物（见 ResolveBasicAttack 顶部说明）。
            // 技能槽的弹道（投矛 / 破甲重箭 / 时钟飞刃）仍然走这里。
            if (slot == AbilitySlot.BasicAttack)
            {
                return;
            }

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
                    Slot = (int)slot,
                    ArrowVisual = definition.Persona == PersonaId.Ranger
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
                Slot = (int)slot,
                ArrowVisual = definition.Persona == PersonaId.Ranger
            });
        }

        /// <summary>
        /// 游侠「缚地荆棘」：在以瞄准点为中心的位置生成 3×1 荆棘地形。
        /// 地形本身不结算伤害，缠绕由 <see cref="ElfRangerBrambleZone"/> 在目标离开时施加。
        /// </summary>
        private void SpawnBrambleZone(AbilityDefinition definition)
        {
            float reach = definition.RangeCells > 0f ? definition.RangeCells : 4f;
            Vec2 center = GetAimWorldPoint(reach);
            Vec2 direction = GetAimDirection();
            float duration = definition.DurationSeconds > 0f ? definition.DurationSeconds : 2f;

            ElfRangerBrambleZone.Spawn(
                new Vector3(center.X, center.Y, 0f),
                new Vector2(direction.X, direction.Y),
                Mathf.Max(1f, definition.AreaLengthCells),
                Mathf.Max(0.5f, definition.AreaWidthCells),
                duration,
                instance.Id);

            // 施法表现：投掷后立刻进入重箭分镜（与「投矛复用突刺」同一处理思路）。
            RangerAnimation?.PlayHeavy(null);

            if (logCasts)
            {
                Debug.Log("[技能] 缚地荆棘 生成于 " + AbilityMath.Number(center.X) + "," + AbilityMath.Number(center.Y), this);
            }
        }

        /// <summary>
        /// 鼠标落点，并夹在 <paramref name="maxDistanceCells"/> 格内；没有有效瞄准时退回正前方。
        /// </summary>
        private Vec2 GetAimWorldPoint(float maxDistanceCells)
        {
            Vec2 origin = ToVec2(transform.position);
            float max = Mathf.Max(0.5f, maxDistanceCells) * unitsPerCell;

            GameInput input = GameInput.Instance;
            if (input != null && input.HasValidAim)
            {
                Vec2 point = ToVec2(input.AimWorldPoint);
                Vec2 offset = point - origin;
                if (offset.SqrMagnitude > 0.001f)
                {
                    return offset.Magnitude > max ? origin + offset.Normalized * max : point;
                }
            }

            return origin + GetAimDirection() * max;
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

            // 突刺分镜同样按施法者选：剑客放别人的本我冲刺也要有「突刺」这一下，
            // 否则位移已经发生、画面上却还是跑步帧，手感等同于「按了没反应」。
            if (TryPlayCasterDash(resolve))
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

            // 点击只负责「通知释放攻击」：挥砍方向沿用角色当前朝向（A/D 决定的左右），
            // 鼠标在另一侧也不转身。结算要等动画命中帧，这里把方向锁在按下瞬间。
            Vec2 aimedDirection = GetFacingDirection();
            System.Action resolve = () => ResolveBasicAttack(definition, aimedDirection);

            SwordsmanSpriteAnimation animation = SwordAnimation;
            if (animation != null && animation.PlayAttack(resolve))
            {
                return true;
            }

            StrongmanSpriteAnimation strongman = StrongmanAnimation;
            if (strongman != null && strongman.PlayPunch(resolve))
            {
                return true;
            }

            // 斯巴达长矛突刺：6 帧 15fps，命中帧 3 = 0.200s，正好是规格里的前摇 0.2s。
            SpartanSpriteAnimation spartan = SpartanAnimation;
            if (spartan != null && spartan.PlayThrust(resolve))
            {
                return true;
            }

            // 游侠短弓：10 帧 20fps，撒放帧 6 = 0.30s，箭在抬手之后才离弦。
            ElfRangerSpriteAnimation ranger = RangerAnimation;
            if (ranger != null && ranger.PlayAttack(resolve))
            {
                return true;
            }

            resolve();
            return true;
        }

        private void ResolveBasicAttack(AbilityDefinition definition, Vec2 aimedDirection)
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            // 平A 一律不生成飞行物（2026-11-28 策划确认：以前远距离人设的普攻会射出
            // 一个白点/箭，判定方向还带竖直分量，看上去就是「发了个东西往斜上方飘」）。
            // 现在改成「动画命中帧 + 沿朝向的瞬时判定」：伤害立刻落在目标身上，
            // 场上不再残留任何独立弹道物体；技能槽的弹道（投矛/重箭/飞刃）保持不变。
            DerivedAbilityStats derived = instance.Derived;
            CharacterInstance target = FindBasicAttackTarget(definition, aimedDirection,
                Mathf.Max(0.5f, derived.AttackRangeCells));
            if (target == null)
            {
                if (logCasts)
                {
                    Debug.Log("[技能] 平A 挥空（" + AbilityMath.Number(derived.AttackRangeCells) + " 格内没有人）", this);
                }

                return;
            }

            BasicAttackHitResult hit = instance.NotifyBasicAttackHit(target.Id);

            // 散弹类（蒸汽铳 4 枚 × 2）原本靠 4 条弹道各算一次；改成瞬时判定后，
            // 多枚弹片在同一个锥形里必然一起命中，这里直接合并成一次结算，
            // 否则派生伤害只有 PelletDamage（2 点），等于把散弹削成单发。
            float damage = derived.BasicAttackDamage;
            if (definition.Shape == AbilityShape.Fan
                && definition.PelletCount > 1
                && definition.PelletDamage > 0f)
            {
                damage = definition.PelletDamage * definition.PelletCount;
            }

            damage += hit.BonusDamage;
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

        /// <summary>
        /// 平A 选目标。技能表给近战平A 写的是「正前方 N 格长 × M 格宽矩形」
        /// （斯巴达长矛是 2×1、剑客 1×2、力士 1×2），所以矩形/扇形必须按朝向选目标：
        /// 只打正前方，挥空就是挥空，不能退化成「射程内最近的人」——
        /// 否则长矛会穿到身后，与技能表脱节。
        /// 没有形状信息（弹道类在更早的分支处理）时退回旧的「射程内最近单体」。
        /// </summary>
        private CharacterInstance FindBasicAttackTarget(AbilityDefinition definition, Vec2 direction,
            float rangeWorld)
        {
            HitShape shape = null;
            // 平A 不再有飞行物：远程人设（游侠短弓 / 钟表飞刃）也要按 Line 形状
            // 沿朝向做一次瞬时判定，否则会退化成「身边最近的人」，箭打到背后。
            if (definition.Shape == AbilityShape.Rect
                || definition.Shape == AbilityShape.Line
                || definition.Shape == AbilityShape.Fan)
            {
                shape = HitShape.FromDefinition(definition);
            }

            // 姿态可以把平A 改短：狂战「单刀」把长矛 2 格压成 1 格（见 CombatStances.BerserkSpec）。
            CombatStanceSpec stance = instance.Combat != null && instance.Combat.Stance.IsActive
                ? instance.Combat.Stance.Current
                : null;
            if (stance != null && stance.BasicAttackRangeOverride > 0f)
            {
                shape = HitShape.Rect(stance.BasicAttackRangeOverride, 1f);
            }

            if (shape != null)
            {
                BuildTargetInfos();
                int id = shape.QueryFirst(ToVec2(transform.position), direction, targetBuffer, instance.Id);
                if (id >= 0)
                {
                    CharacterInstance shaped = FindInstance(id);
                    if (shaped != null && !shaped.IsDead)
                    {
                        return shaped;
                    }
                }

                return null;
            }

            return FindNearestTarget(rangeWorld);
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
            Vec2 direction = body != null ? body.FacingVector : Vec2.Right;

            GameInput input = GameInput.Instance;
            if (input != null && input.HasValidAim)
            {
                Vec2 aim = ToVec2(input.AimWorldPoint) - origin;
                if (aim.SqrMagnitude > 0.001f)
                {
                    direction = aim.Normalized;
                }
            }

            // 横版地面锁定（CombatBody.lockToGround）下角色只有左右两个朝向：
            // 鼠标是投到 z=0 世界平面上的，角色站在地面线（比平面低 3~4 个单位）时，
            // 这个方向天然带一大截竖直分量 —— 不压平的话技能会朝斜上方判定、
            // 技能弹道也会「往斜上方飘」。这里按鼠标在左/右决定正负，Y 强制清零。
            if (body != null && body.IsGroundLocked)
            {
                direction = new Vec2(direction.X >= 0f ? 1f : -1f, 0f);
            }

            // 输入瞄准、命中判定与精灵水平翻转统一使用同一个方向。
            body?.SetFacing(direction);
            return direction;
        }

        /// <summary>
        /// 角色当前朝向（横版地面锁定下压成左右单位向量）。
        /// 平A 只用它：点击是「通知释放攻击」，不参与左右朝向的决定。
        /// </summary>
        private Vec2 GetFacingDirection()
        {
            Vec2 direction = body != null ? body.FacingVector : Vec2.Right;
            if (body != null && body.IsGroundLocked)
            {
                direction = new Vec2(direction.X >= 0f ? 1f : -1f, 0f);
            }

            return direction;
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

                    // 游侠的箭用 ElfRanger_Arrow_* 序列帧做弹道视觉（12 帧 30fps 循环）；
                    // 其他人设的弹道保持默认白点。判定看技能所属人设而不是施法者造型：
                    // 形象跟着特质走，游侠属性的玩家可能正顶着剑士的造型在放弓。
                    if (state.Spec.ArrowVisual)
                    {
                        Sprite[] arrow = ArrowFrames();
                        if (arrow != null && arrow.Length > 0)
                        {
                            view.ApplyFrames(arrow, 30f, 1.25f, state.Spec.Direction.X < 0f);
                        }
                    }

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

        // 形象会随特质换人设，旧的序列帧组件会被销毁：缓存引用变成 Unity null 时重新找。
        private SwordsmanSpriteAnimation SwordAnimation
        {
            get
            {
                if (swordAnimation == null)
                {
                    swordAnimation = GetComponentInChildren<SwordsmanSpriteAnimation>();
                }

                return swordAnimation;
            }
        }

        private StrongmanSpriteAnimation StrongmanAnimation
        {
            get
            {
                if (strongmanAnimation == null)
                {
                    strongmanAnimation = GetComponentInChildren<StrongmanSpriteAnimation>();
                }

                return strongmanAnimation;
            }
        }

        private SpartanSpriteAnimation SpartanAnimation
        {
            get
            {
                if (spartanAnimation == null)
                {
                    spartanAnimation = GetComponentInChildren<SpartanSpriteAnimation>();
                }

                return spartanAnimation;
            }
        }

        private ElfRangerSpriteAnimation RangerAnimation
        {
            get
            {
                if (rangerAnimation == null)
                {
                    rangerAnimation = GetComponentInChildren<ElfRangerSpriteAnimation>();
                }

                return rangerAnimation;
            }
        }

        /// <summary>弹道视觉用的箭矢序列帧（只加载一次，全角色共用）。</summary>
        private Sprite[] ArrowFrames()
        {
            if (arrowFramesLoaded)
            {
                return arrowFrames;
            }

            arrowFramesLoaded = true;
            Sprite[] all = Resources.LoadAll<Sprite>("ElfRanger");
            if (all == null || all.Length == 0)
            {
                return null;
            }

            var picked = new List<Sprite>(12);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.StartsWith("ElfRanger_Arrow_", System.StringComparison.Ordinal))
                {
                    picked.Add(all[i]);
                }
            }

            picked.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            arrowFrames = picked.ToArray();
            return arrowFrames;
        }

        // ---------------- 动作表现（与判定对齐） ----------------

        /// <summary>
        /// 用「施法者自己」的序列帧播一次攻击表现，命中帧再回调结算。
        ///
        /// 关键点：动画按**施法者**选，不按技能的来源人设选。
        /// 拆卡混装是本作核心机制——剑客完全可以装着力士的本我技能，
        /// 若这里按 definition.Persona 选动画，剑客放力士技能就一套动作都没有，
        /// 玩家只看到血条掉了、人没动，表现为「技能没反应」。
        /// </summary>
        private bool TryPlayCasterAction(System.Action onImpact)
        {
            SwordsmanSpriteAnimation swordsman = SwordAnimation;
            if (swordsman != null && swordsman.PlayAttack(onImpact))
            {
                return true;
            }

            StrongmanSpriteAnimation strongman = StrongmanAnimation;
            if (strongman != null && strongman.PlayPunch(onImpact))
            {
                return true;
            }

            // 斯巴达：本我「战阵横扫」等矩形技能走横扫分镜，命中帧 = 弧光帧（3 / 12fps = 0.25s）。
            // 放进来之后，这类技能不再是「按下瞬间第 0 帧结算、人物纹丝不动」。
            SpartanSpriteAnimation spartan = SpartanAnimation;
            if (spartan != null && spartan.PlaySweep(onImpact))
            {
                return true;
            }

            // 游侠：混装别的近战技能时用「重箭抬手」顶一段动作，避免血条掉了人不动。
            ElfRangerSpriteAnimation ranger = RangerAnimation;
            if (ranger != null && ranger.PlayHeavy(onImpact))
            {
                return true;
            }

            return false;
        }

        /// <summary>同 <see cref="TryPlayCasterAction"/>，但走突刺分镜。</summary>
        private bool TryPlayCasterDash(System.Action onImpact)
        {
            SwordsmanSpriteAnimation swordsman = SwordAnimation;
            if (swordsman != null && swordsman.PlayDash(onImpact))
            {
                return true;
            }

            StrongmanSpriteAnimation strongman = StrongmanAnimation;
            if (strongman != null && strongman.PlayDash(onImpact))
            {
                return true;
            }

            // 游侠没有位移序列帧，用前闪分镜顶替。
            ElfRangerSpriteAnimation ranger = RangerAnimation;
            if (ranger != null && ranger.PlayDodge(true))
            {
                onImpact?.Invoke();
                return true;
            }

            return false;
        }
    }
}
