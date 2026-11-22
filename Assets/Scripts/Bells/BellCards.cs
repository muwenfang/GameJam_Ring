using System;
using System.Collections.Generic;
using Ring.Characters;

namespace Ring.Bells
{
    /// <summary>
    /// 特质卡。文档设定 6 个特质、恰好对应 6 个人设，所以特质卡无需额外 ID，
    /// 「哪个人设的特质」本身就是这张卡的身份。见 数据和结构.txt / 钟声_全文.txt 的特质列表。
    /// </summary>
    public readonly struct TraitCard : IEquatable<TraitCard>
    {
        public TraitCard(PersonaId persona)
        {
            Persona = persona;
        }

        public PersonaId Persona { get; }

        public bool IsValid
        {
            get { return PersonaIds.IsDefined(Persona); }
        }

        /// <summary>例如「特质：剑客」。</summary>
        public string ToDisplayName()
        {
            return "特质：" + PersonaIds.ToChineseName(Persona);
        }

        public bool Equals(TraitCard other)
        {
            return Persona == other.Persona;
        }

        public override bool Equals(object obj)
        {
            return obj is TraitCard other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)Persona;
        }

        public override string ToString()
        {
            return ToDisplayName();
        }

        public static bool operator ==(TraitCard left, TraitCard right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TraitCard left, TraitCard right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// 技能卡 = (人设, 等级)。一人每个等级持有一个技能，
    /// 所以「同一人设 + 同一等级」唯一确定一张技能卡。
    /// </summary>
    public readonly struct SkillCard : IEquatable<SkillCard>
    {
        public SkillCard(PersonaId persona, SkillTier tier)
        {
            Persona = persona;
            Tier = tier;
        }

        public PersonaId Persona { get; }

        public SkillTier Tier { get; }

        public bool IsValid
        {
            get { return PersonaIds.IsDefined(Persona) && BellCards.IsValidTier(Tier); }
        }

        /// <summary>例如「技能：剑客·本我」。</summary>
        public string ToDisplayName()
        {
            return "技能：" + PersonaIds.ToChineseName(Persona) + "·" + Tier.ToDisplayName();
        }

        public bool Equals(SkillCard other)
        {
            return Persona == other.Persona && Tier == other.Tier;
        }

        public override bool Equals(object obj)
        {
            return obj is SkillCard other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Persona * 8) + (int)Tier;
        }

        public override string ToString()
        {
            return ToDisplayName();
        }

        public static bool operator ==(SkillCard left, SkillCard right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SkillCard left, SkillCard right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// 卡牌集合的纯函数工具：整副牌的定义、「集齐完整人设」的判定。
    /// 「集齐」的定义（文档 B13/B14）：同一个人的特质 + 三个等级的技能各一个。
    /// </summary>
    public static class BellCards
    {
        /// <summary>6 个人设 × 1 个特质 = 6 张特质卡。</summary>
        public static IReadOnlyList<TraitCard> CreateAllTraits()
        {
            var list = new List<TraitCard>(PersonaIds.Count);
            for (int i = 0; i < PersonaIds.Count; i++)
            {
                list.Add(new TraitCard((PersonaId)i));
            }

            return list;
        }

        /// <summary>6 个人设 × 3 个等级 = 18 张技能卡。</summary>
        public static IReadOnlyList<SkillCard> CreateAllSkills()
        {
            var list = new List<SkillCard>(PersonaIds.Count * 3);
            for (int tier = 1; tier <= 3; tier++)
            {
                for (int i = 0; i < PersonaIds.Count; i++)
                {
                    list.Add(new SkillCard((PersonaId)i, (SkillTier)tier));
                }
            }

            return list;
        }

        public static bool IsValidTier(SkillTier tier)
        {
            return tier == SkillTier.Id || tier == SkillTier.Ego || tier == SkillTier.Superego;
        }

        public static readonly SkillTier[] Tiers =
        {
            SkillTier.Id,
            SkillTier.Ego,
            SkillTier.Superego,
        };

        /// <summary>该人设的三张技能卡是否都在这批技能卡里（同一等级各一张）。</summary>
        public static bool HasAllTiers(IEnumerable<SkillCard> skills, PersonaId persona)
        {
            if (skills == null)
            {
                return false;
            }

            int mask = 0;
            foreach (SkillCard card in skills)
            {
                if (card.Persona == persona)
                {
                    mask |= 1 << (int)card.Tier;
                }
            }

            return mask == FullTierMask;
        }

        private const int FullTierMask = (1 << 1) | (1 << 2) | (1 << 3);

        public static bool HasTrait(IEnumerable<TraitCard> traits, PersonaId persona)
        {
            if (traits == null)
            {
                return false;
            }

            foreach (TraitCard card in traits)
            {
                if (card.Persona == persona)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 是否集齐一套完整人设（特质 + 三个等级技能），并给出是哪个人设。
        /// 若同时集齐多个（理论上不应该发生），返回枚举序最小的那个。
        /// </summary>
        public static bool TryGetCompletePersona(
            IReadOnlyList<TraitCard> traits,
            IReadOnlyList<SkillCard> skills,
            out PersonaId persona)
        {
            persona = default;
            if (traits == null || skills == null)
            {
                return false;
            }

            for (int i = 0; i < PersonaIds.Count; i++)
            {
                PersonaId candidate = (PersonaId)i;
                if (HasTrait(traits, candidate) && HasAllTiers(skills, candidate))
                {
                    persona = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool HasCompleteSet(IReadOnlyList<TraitCard> traits, IReadOnlyList<SkillCard> skills)
        {
            PersonaId ignored;
            return TryGetCompletePersona(traits, skills, out ignored);
        }

        /// <summary>距离集齐还差什么，用于 UI 提示（例如「缺 剑客·超我」）。</summary>
        public static string DescribeProgress(
            IReadOnlyList<TraitCard> traits,
            IReadOnlyList<SkillCard> skills,
            PersonaId target)
        {
            var missing = new List<string>(4);
            if (!HasTrait(traits, target))
            {
                missing.Add(new TraitCard(target).ToDisplayName());
            }

            for (int i = 0; i < Tiers.Length; i++)
            {
                var card = new SkillCard(target, Tiers[i]);
                bool found = false;
                if (skills != null)
                {
                    for (int j = 0; j < skills.Count; j++)
                    {
                        if (skills[j].Equals(card))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    missing.Add(card.ToDisplayName());
                }
            }

            if (missing.Count == 0)
            {
                return PersonaIds.ToChineseName(target) + "：已集齐";
            }

            return PersonaIds.ToChineseName(target) + "：还缺 " + string.Join("、", missing);
        }
    }
}
