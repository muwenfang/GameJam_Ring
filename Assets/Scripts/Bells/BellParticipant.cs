using System.Collections.Generic;
using Ring.Characters;

namespace Ring.Bells
{
    /// <summary>
    /// IBellParticipant 的默认实现：只保存钟声相关状态的纯 C# 对象。
    /// 离线逻辑测试直接用它；正式的角色脚本可以自己实现接口，也可以包一层委托到这里。
    /// </summary>
    public sealed class BellParticipant : IBellParticipant
    {
        private readonly List<TraitCard> traits = new List<TraitCard>(1);
        private readonly List<SkillCard> skills = new List<SkillCard>(3);

        public BellParticipant(int id, string displayName)
        {
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? "角色" + id : displayName;
            IsInPlay = true;
            IsAlive = true;
        }

        public int Id { get; }

        public string DisplayName { get; set; }

        public bool IsInPlay { get; set; }

        public bool IsAlive { get; set; }

        public bool IsImmuneToRedistribution { get; set; }

        public IReadOnlyList<TraitCard> Traits
        {
            get { return traits; }
        }

        public IReadOnlyList<SkillCard> Skills
        {
            get { return skills; }
        }

        /// <summary>当前是否持有完整人设（特质 + 三个等级技能）。</summary>
        public bool HasCompleteSet
        {
            get { return BellCards.HasCompleteSet(traits, skills); }
        }

        /// <summary>集齐的是哪个人设；未集齐时返回 default。</summary>
        public PersonaId CompletedPersona
        {
            get
            {
                PersonaId persona;
                return BellCards.TryGetCompletePersona(traits, skills, out persona) ? persona : default;
            }
        }

        public bool AddTrait(TraitCard card)
        {
            if (!card.IsValid || traits.Contains(card))
            {
                return false;
            }

            traits.Add(card);
            return true;
        }

        public bool RemoveTrait(TraitCard card)
        {
            return traits.Remove(card);
        }

        public bool AddSkill(SkillCard card)
        {
            if (!card.IsValid || skills.Contains(card))
            {
                return false;
            }

            skills.Add(card);
            return true;
        }

        public bool RemoveSkill(SkillCard card)
        {
            return skills.Remove(card);
        }

        public void ClearCards()
        {
            traits.Clear();
            skills.Clear();
        }

        /// <summary>「剑客（特质：坚韧）/ 技能：剑客·本我、剑客·自我」这样的可读摘要。</summary>
        public string Describe()
        {
            var parts = new List<string>(traits.Count + skills.Count + 2);
            parts.Add(DisplayName);
            parts.Add(IsInPlay ? (IsAlive ? "在场" : "在场·已阵亡") : "不在场");
            if (IsImmuneToRedistribution)
            {
                parts.Add("已集齐·免疫钟声分配");
            }

            for (int i = 0; i < traits.Count; i++)
            {
                parts.Add(traits[i].ToDisplayName());
            }

            for (int i = 0; i < skills.Count; i++)
            {
                parts.Add(skills[i].ToDisplayName());
            }

            return string.Join(" | ", parts);
        }

        public override string ToString()
        {
            return Describe();
        }
    }
}
