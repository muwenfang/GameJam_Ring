using System;
using System.Collections.Generic;
using Ring.Bells;
using Ring.Characters;

namespace Ring.Abilities
{
    /// <summary>
    /// 一张能力卡 = (人设, 槽位)。这是「五个能力可以分开到不同角色身上」的载体：
    ///   剑客·特质 + 力士·普攻 + 钟表师·本我 + 剑客·自我 + 游侠·超我
    /// 拼在同一个人身上是合法的，而一张卡永远属于某个人设的某个槽位。
    /// </summary>
    public readonly struct AbilityCard : IEquatable<AbilityCard>
    {
        public AbilityCard(PersonaId persona, AbilitySlot slot)
        {
            Persona = persona;
            Slot = slot;
        }

        public PersonaId Persona { get; }

        public AbilitySlot Slot { get; }

        public bool IsValid
        {
            get { return PersonaIds.IsDefined(Persona) && Slot.IsDefined(); }
        }

        /// <summary>对应的数值定义；未定义时返回 null。</summary>
        public AbilityDefinition Definition
        {
            get { return AbilityCatalog.Get(Persona, Slot); }
        }

        /// <summary>例如「剑客·本我」。</summary>
        public string ToDisplayName()
        {
            return PersonaIds.ToChineseName(Persona) + "·" + Slot.ToDisplayName();
        }

        /// <summary>例如「剑客·本我 突进斩」。</summary>
        public string ToRichName()
        {
            AbilityDefinition definition = Definition;
            return definition == null ? ToDisplayName() : definition.ToDisplayName();
        }

        public bool Equals(AbilityCard other)
        {
            return Persona == other.Persona && Slot == other.Slot;
        }

        public override bool Equals(object obj)
        {
            return obj is AbilityCard other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Persona * AbilitySlots.Count) + (int)Slot;
        }

        public override string ToString()
        {
            return ToDisplayName();
        }

        public static bool operator ==(AbilityCard left, AbilityCard right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AbilityCard left, AbilityCard right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// 能力卡的纯函数工具：整副 30 张的定义、与钟声卡（特质/技能）的互转、「集齐人设」的判定。
    ///
    /// 「集齐完整人设」的口径：钟声文档 B13/B14 明确写的是「特质 + 技能」，
    /// 所以默认要求 特质 + 本我/自我/超我；普攻不在集齐条件里。
    /// 如果策划改成「五个槽都要」，把 includeBasicAttack 传 true 即可，全系统一致生效。
    /// </summary>
    public static class AbilityCards
    {
        /// <summary>整副牌的张数。</summary>
        public const int TotalCount = AbilityCatalog.TotalCount;

        /// <summary>30 张卡，顺序为「人设 → 槽位」。</summary>
        public static IReadOnlyList<AbilityCard> CreateAll()
        {
            var list = new List<AbilityCard>(TotalCount);
            for (int p = 0; p < PersonaIds.Count; p++)
            {
                for (int s = 0; s < AbilitySlots.Count; s++)
                {
                    list.Add(new AbilityCard((PersonaId)p, AbilitySlots.All[s]));
                }
            }

            return list;
        }

        /// <summary>某个人设的 5 张卡（一整「套」）。</summary>
        public static IReadOnlyList<AbilityCard> CreatePersonaSet(PersonaId persona)
        {
            var list = new List<AbilityCard>(AbilitySlots.Count);
            for (int s = 0; s < AbilitySlots.Count; s++)
            {
                list.Add(new AbilityCard(persona, AbilitySlots.All[s]));
            }

            return list;
        }

        /// <summary>特质卡 → 能力卡（特质槽）。</summary>
        public static AbilityCard FromTrait(TraitCard card)
        {
            return new AbilityCard(card.Persona, AbilitySlot.Trait);
        }

        /// <summary>技能卡 → 能力卡（本我/自我/超我）。</summary>
        public static AbilityCard FromSkill(SkillCard card)
        {
            return new AbilityCard(card.Persona, AbilitySlots.FromSkillTier(card.Tier));
        }

        /// <summary>能力卡 → 钟声特质卡；不是特质槽时返回 false。</summary>
        public static bool TryAsTrait(AbilityCard card, out TraitCard trait)
        {
            if (card.Slot == AbilitySlot.Trait)
            {
                trait = new TraitCard(card.Persona);
                return true;
            }

            trait = default;
            return false;
        }

        /// <summary>能力卡 → 钟声技能卡；不是技能槽时返回 false。</summary>
        public static bool TryAsSkill(AbilityCard card, out SkillCard skill)
        {
            if (card.Slot.IsSkill())
            {
                skill = new SkillCard(card.Persona, card.Slot.ToSkillTier());
                return true;
            }

            skill = default;
            return false;
        }

        /// <summary>这批卡里属于某人设的有几张（用于 UI 的「3/5」进度）。</summary>
        public static int CountFor(IEnumerable<AbilityCard> cards, PersonaId persona)
        {
            if (cards == null)
            {
                return 0;
            }

            int count = 0;
            foreach (AbilityCard card in cards)
            {
                if (card.Persona == persona)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>某人设的关键槽位是否都在。</summary>
        public static bool HasSlot(IEnumerable<AbilityCard> cards, PersonaId persona, AbilitySlot slot)
        {
            if (cards == null)
            {
                return false;
            }

            var wanted = new AbilityCard(persona, slot);
            foreach (AbilityCard card in cards)
            {
                if (card == wanted)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 是否集齐某人设的一套。默认是「特质 + 三个技能」，
        /// <paramref name="includeBasicAttack"/> 为 true 时还要求普攻。
        /// </summary>
        public static bool IsCompleteSet(IEnumerable<AbilityCard> cards, PersonaId persona, bool includeBasicAttack = false)
        {
            if (!HasSlot(cards, persona, AbilitySlot.Trait))
            {
                return false;
            }

            if (includeBasicAttack && !HasSlot(cards, persona, AbilitySlot.BasicAttack))
            {
                return false;
            }

            return HasSlot(cards, persona, AbilitySlot.Id)
                && HasSlot(cards, persona, AbilitySlot.Ego)
                && HasSlot(cards, persona, AbilitySlot.Superego);
        }

        /// <summary>
        /// 找出已集齐的人设；同时集齐多个时取枚举序最小的一个（理论上不应该发生）。
        /// </summary>
        public static bool TryGetCompletePersona(
            IEnumerable<AbilityCard> cards,
            out PersonaId persona,
            bool includeBasicAttack = false)
        {
            persona = default;
            if (cards == null)
            {
                return false;
            }

            var owned = new List<AbilityCard>(cards);
            for (int i = 0; i < PersonaIds.Count; i++)
            {
                if (IsCompleteSet(owned, (PersonaId)i, includeBasicAttack))
                {
                    persona = (PersonaId)i;
                    return true;
                }
            }

            return false;
        }

        public static bool HasCompleteSet(IEnumerable<AbilityCard> cards, bool includeBasicAttack = false)
        {
            PersonaId ignored;
            return TryGetCompletePersona(cards, out ignored, includeBasicAttack);
        }

        /// <summary>距离集齐还缺哪些槽位，用于 HUD 提示。</summary>
        public static string DescribeProgress(IEnumerable<AbilityCard> cards, PersonaId persona, bool includeBasicAttack = false)
        {
            var owned = new List<AbilityCard>(cards ?? new AbilityCard[0]);
            var missing = new List<string>(AbilitySlots.Count);

            for (int s = 0; s < AbilitySlots.Count; s++)
            {
                AbilitySlot slot = AbilitySlots.All[s];
                if (slot == AbilitySlot.BasicAttack && !includeBasicAttack)
                {
                    continue;
                }

                if (!HasSlot(owned, persona, slot))
                {
                    missing.Add(slot.ToDisplayName());
                }
            }

            if (missing.Count == 0)
            {
                return PersonaIds.ToChineseName(persona) + "：已集齐";
            }

            return PersonaIds.ToChineseName(persona) + "：还缺 " + string.Join("、", missing);
        }
    }
}
