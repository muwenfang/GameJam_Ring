using Ring.Abilities;
using Ring.Bells;
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

        private CharacterInstance instance;

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

            if (logCasts)
            {
                var definition = activation.Definition;
                Debug.Log("[技能] 施放 " + definition.ToDisplayName()
                    + "（冷却 " + AbilityMath.Number(activation.CooldownSeconds) + "s"
                    + (activation.HealAmount > 0f ? "，治疗 " + AbilityMath.Number(activation.HealAmount) : string.Empty)
                    + (activation.ShieldAmount > 0f ? "，护盾 " + AbilityMath.Number(activation.ShieldAmount) : string.Empty)
                    + "）", this);
            }

            return true;
        }

        /// <summary>打一次平A：射程内最近的活人吃伤害，附加效果按特质结算。</summary>
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

            DerivedAbilityStats derived = instance.Derived;
            float range = Mathf.Max(0.5f, derived.AttackRangeCells) * unitsPerCell;
            CharacterInstance target = FindNearestTarget(range);
            if (target == null)
            {
                if (logCasts)
                {
                    Debug.Log("[技能] 平A：" + activation.Definition.AbilityName + " 挥空了（" + AbilityMath.Number(range) + " 格内没有人）。", this);
                }

                return true;
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

            return true;
        }

        private CharacterInstance FindNearestTarget(float rangeWorld)
        {
            CharacterInstance[] all = FindObjectsOfType<CharacterInstance>();
            CharacterInstance best = null;
            float bestDistance = float.MaxValue;

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
