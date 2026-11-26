using Ring.Abilities;
using Ring.Bells;
using Ring.Maps;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 玩家侧的「把按键接到五个能力槽」的演示组件。
    ///
    /// 只做三件事：按 1/2/3 施放本我/自我/超我；按左键打一次平A；把结果打印出来。
    /// 命中判定是最朴素的一种（射程内最近的一个活人），真正的战斗层接进来后可以直接替换掉本类。
    ///
    /// 它同时演示了「技能 → 人物属性」的衔接：
    /// 移速、攻击间隔、减伤、回血、护盾都由 <see cref="AbilityRuntime"/> 算好后，经
    /// <see cref="CharacterInstance"/> 生效，本类不自己改任何数值。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterInstance))]
    [AddComponentMenu("钟声/玩家技能 AbilityCaster")]
    public sealed class AbilityCaster : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("一格对应多少世界单位（与 PlayerMotor 的 unitsPerCell 保持一致）。")]
        private float unitsPerCell = 1f;

        [SerializeField]
        [Tooltip("是否允许鼠标左键打出一次平A。")]
        private bool enableBasicAttack = true;

        [SerializeField]
        [Tooltip("是否把施放与命中结果打印到 Console。")]
        private bool logCasts = true;

        [SerializeField]
        [Tooltip("D12：施放超我（游侠「缚地荆棘」是唯一鼠标指定位置技能）时，把本帧选点格记进日志与 LastAimCell。只做记录，不改任何伤害数值。")]
        private bool logAimPointForSuperego = true;

        /// <summary>最近一次超我施放时采到的选点格（供 HUD / M3 命中判定读取）。</summary>
        public Ring.Inputs.GridCoord LastAimCell { get; private set; }

        /// <summary><see cref="LastAimCell"/> 是否已经采到过有效值。</summary>
        public bool HasLastAimCell { get; private set; }

        private CharacterInstance instance;

        private Ring.Characters.SwordsmanSpriteAnimation swordAnimation;
        private bool swordAnimationSearched;

        private void Awake()
        {
            instance = GetComponent<CharacterInstance>();
        }

        private void Update()
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            var input = Ring.Inputs.GameInput.Instance;
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

        /// <summary>施放一个技能槽；返回是否真的放出去了（冷却中/没卡都会返回 false）。</summary>
        public bool Cast(AbilitySlot slot)
        {
            if (instance == null || instance.Abilities == null)
            {
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

            if (activation.HealAmount > 0f)
            {
                instance.Heal(activation.HealAmount);
            }

            // 只有「实际施放的是剑士本我」才走突进斩。
            // 拆卡混装时玩家可能拿着别人人设的本我卡，此时按 instance.Persona 判断会
            // 误播剑士突刺、并套用 3×2 命中框（别人技能多半没有 DashCells / Area 配置）。
            if (slot == AbilitySlot.Id && activation.Definition != null
                && activation.Definition.Persona == Ring.Characters.PersonaId.Swordsman)
            {
                ExecuteSwordsmanDash(activation.Definition);
            }

            // D12：超我槽是「鼠标指定位置」技能的落点（游侠·缚地荆棘）。
            // 这里只把 Intent.aimPoint 记下来并打印，命中判定与伤害仍走原有逻辑，
            // 等 M3 真正实现 3×1 荆棘地形时直接读 LastAimCell / Intent.AimCell 即可。
            string aimNote = string.Empty;
            if (slot == AbilitySlot.Superego && logAimPointForSuperego)
            {
                Ring.Inputs.GameInput input = Ring.Inputs.GameInput.Instance;
                if (input != null && input.HasValidAim)
                {
                    Ring.Inputs.GridCoord cell = input.AimCell;
                    LastAimCell = cell;
                    HasLastAimCell = true;
                    aimNote = "，选点 格" + cell + "（世界 " + input.AimWorldPoint.ToString("F2")
                              + (input.AimInsideGrid ? string.Empty : "，地图外") + "）";
                }
                else
                {
                    aimNote = "，选点无效（相机未指派 / 鼠标压在 UI 上）";
                }
            }

            if (logCasts)
            {
                var definition = activation.Definition;
                Debug.Log("[技能] 施放 " + definition.ToDisplayName()
                    + "（冷却 " + AbilityMath.Number(activation.CooldownSeconds) + "s"
                    + (activation.HealAmount > 0f ? "，治疗 " + AbilityMath.Number(activation.HealAmount) : string.Empty)
                    + (activation.ShieldAmount > 0f ? "，护盾 " + AbilityMath.Number(activation.ShieldAmount) : string.Empty)
                    + "）" + aimNote, this);
            }

            return true;
        }

        private void ExecuteSwordsmanDash(AbilityDefinition definition)
        {
            Ring.Inputs.GameInput input = Ring.Inputs.GameInput.Instance;
            Vector2 direction = input != null ? input.MoveAxis : Vector2.zero;
            if (direction.sqrMagnitude < 0.001f)
            {
                var renderer = SwordAnimation != null ? SwordAnimation.Renderer : null;
                direction = renderer != null && renderer.flipX ? Vector2.left : Vector2.right;
            }
            direction.Normalize();

            Vector2 start = transform.position;
            float dashDistance = Mathf.Max(0f, definition.DashCells) * unitsPerCell;
            transform.position = start + direction * dashDistance;

            // 命中判定挂到突刺的刀光爆发帧；没有动画组件 / 帧缺失时立即结算。
            // 位移仍是既有的一次性瞬移（见 Docs/07 第八节），本次只修「判定与动画不同步」。
            AbilityDefinition def = definition;
            Vector2 origin = start;
            Vector2 dir = direction;
            float distance = dashDistance;
            System.Action resolve = () => ResolveDashHit(def, origin, dir, distance);

            Ring.Characters.SwordsmanSpriteAnimation animation = SwordAnimation;
            if (animation != null && animation.PlayDash(resolve))
            {
                return;
            }

            resolve();
        }

        /// <summary>突进路程与终点前一格组成的矩形命中框；用朝向轴投影支持斜向输入。</summary>
        private void ResolveDashHit(AbilityDefinition definition, Vector2 start, Vector2 direction, float dashDistance)
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            float halfLength = Mathf.Max(0.5f, definition.AreaLengthCells * unitsPerCell * 0.5f);
            float halfWidth = Mathf.Max(0.5f, definition.AreaWidthCells * unitsPerCell * 0.5f);
            Vector2 center = start + direction * (dashDistance * 0.5f + unitsPerCell * 0.5f);
            CharacterInstance[] targets = FindObjectsOfType<CharacterInstance>();
            for (int i = 0; i < targets.Length; i++)
            {
                CharacterInstance target = targets[i];
                if (target == null || target == instance || target.IsDead
                    || (target.Participant != null && !target.Participant.IsInPlay)) continue;
                Vector2 offset = (Vector2)target.transform.position - center;
                float along = Vector2.Dot(offset, direction);
                float across = Mathf.Abs(Vector2.Dot(offset, new Vector2(-direction.y, direction.x)));
                if (Mathf.Abs(along) > halfLength || across > halfWidth) continue;

                float previousHealth = target.CurrentHealth;
                target.TakeDamage(definition.Damage);
                if (target.CurrentHealth < previousHealth)
                    instance.NotifyDamageDealt();
            }
        }

        /// <summary>
        /// 打一次平A：先扣攻击间隔并起挥剑动作，伤害在刀光帧结算（射程内最近的活人）。
        /// 没有剑士动画组件 / 帧缺失时退化为立即结算，保证不会「按了没反应」。
        /// </summary>
        public bool BasicAttack()
        {
            if (instance == null || instance.Abilities == null)
            {
                return false;
            }

            AbilityActivation activation = instance.Abilities.TryActivate(AbilitySlot.BasicAttack);
            if (!activation.Success)
            {
                return false;
            }

            AbilityDefinition definition = activation.Definition;
            System.Action resolve = () => ResolveBasicAttackHit(definition);

            Ring.Characters.SwordsmanSpriteAnimation animation = SwordAnimation;
            if (animation != null && animation.PlayAttack(resolve))
            {
                return true;
            }

            resolve();
            return true;
        }

        /// <summary>挥剑刀光帧到达时结算一次平A（含特质附加效果与击退）。</summary>
        private void ResolveBasicAttackHit(AbilityDefinition definition)
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            DerivedAbilityStats derived = instance.Derived;
            float range = Mathf.Max(0.5f, derived.AttackRangeCells) * unitsPerCell;
            CharacterInstance target = FindNearestTarget(range);
            if (target == null)
            {
                if (logCasts)
                {
                    Debug.Log("[技能] 平A："
                        + (definition != null ? definition.AbilityName : "普攻")
                        + " 挥空了（" + AbilityMath.Number(range) + " 格内没有人）。", this);
                }

                return;
            }

            BasicAttackHitResult hit = instance.NotifyBasicAttackHit(target.Id);
            float damage = derived.BasicAttackDamage + hit.BonusDamage;
            target.TakeDamage(damage);
            instance.NotifyDamageDealt();

            if (hit.KnockbackCells > 0f)
            {
                Vector3 direction = (target.transform.position - transform.position).normalized;
                target.transform.position += direction * (hit.KnockbackCells * unitsPerCell);
            }

            if (logCasts)
            {
                Debug.Log("[技能] 平A 命中 " + target.DisplayName + "，伤害 " + AbilityMath.Number(damage)
                    + "（" + hit + "）", this);
            }
        }

        /// <summary>剑士序列帧组件（挂在 Visual 子物体上），只在首次用到时查找一次。</summary>
        private Ring.Characters.SwordsmanSpriteAnimation SwordAnimation
        {
            get
            {
                if (!swordAnimationSearched)
                {
                    swordAnimationSearched = true;
                    swordAnimation = GetComponentInChildren<Ring.Characters.SwordsmanSpriteAnimation>();
                }

                return swordAnimation;
            }
        }

        private CharacterInstance FindNearestTarget(float rangeWorld)
        {
            CharacterInstance[] all = FindObjectsOfType<CharacterInstance>();
            CharacterInstance best = null;
            float bestDistance = float.MaxValue;

            // 三场 1v1 并行：平A 只允许命中「本场对手」，不能跨区域打到别场的人。
            // ArenaPlan 不存在时（主菜单、未开局）退回原来的「射程内最近目标」行为。
            ArenaPlan plan = GameFlow.Instance != null ? GameFlow.Instance.CurrentPlan : null;
            int pairedOpponentId = plan != null && instance != null && plan.Contains(instance.Id)
                ? plan.OpponentOf(instance.Id)
                : -1;

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

                if (pairedOpponentId >= 0 && candidate.Id != pairedOpponentId)
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
    }
}
