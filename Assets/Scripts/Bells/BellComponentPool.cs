using System.Collections.Generic;
using Ring.Core;

namespace Ring.Bells
{
    /// <summary>
    /// 组件池。B11：被淘汰者的所有技能与特质落入组件池，胜者可以从中选择一个带走；
    /// 若放弃，则恢复 20% 生命（回血规则在 BellRules.EliminationHealPercent，由上层结算）。
    /// 组件池本身只负责存牌与取牌。
    /// </summary>
    public sealed class BellComponentPool
    {
        private readonly List<TraitCard> traits = new List<TraitCard>(4);
        private readonly List<SkillCard> skills = new List<SkillCard>(8);

        public IReadOnlyList<TraitCard> Traits
        {
            get { return traits; }
        }

        public IReadOnlyList<SkillCard> Skills
        {
            get { return skills; }
        }

        public int TraitCount
        {
            get { return traits.Count; }
        }

        public int SkillCount
        {
            get { return skills.Count; }
        }

        public int TotalCount
        {
            get { return traits.Count + skills.Count; }
        }

        public bool IsEmpty
        {
            get { return traits.Count == 0 && skills.Count == 0; }
        }

        public void AddTrait(TraitCard card)
        {
            if (!traits.Contains(card))
            {
                traits.Add(card);
            }
        }

        public void AddSkill(SkillCard card)
        {
            if (!skills.Contains(card))
            {
                skills.Add(card);
            }
        }

        public void AddAll(IReadOnlyList<TraitCard> traitCards, IReadOnlyList<SkillCard> skillCards)
        {
            if (traitCards != null)
            {
                for (int i = 0; i < traitCards.Count; i++)
                {
                    AddTrait(traitCards[i]);
                }
            }

            if (skillCards != null)
            {
                for (int i = 0; i < skillCards.Count; i++)
                {
                    AddSkill(skillCards[i]);
                }
            }
        }

        /// <summary>把一个被淘汰者身上的所有卡收进组件池，返回收了多少张。</summary>
        public int StoreAllFrom(IBellParticipant eliminated)
        {
            if (eliminated == null)
            {
                return 0;
            }

            int stored = 0;
            // 先复制一份再删，避免边遍历边修改对方的列表。
            var traitSnapshot = new List<TraitCard>(eliminated.Traits);
            for (int i = 0; i < traitSnapshot.Count; i++)
            {
                if (eliminated.RemoveTrait(traitSnapshot[i]))
                {
                    AddTrait(traitSnapshot[i]);
                    stored++;
                }
            }

            var skillSnapshot = new List<SkillCard>(eliminated.Skills);
            for (int i = 0; i < skillSnapshot.Count; i++)
            {
                if (eliminated.RemoveSkill(skillSnapshot[i]))
                {
                    AddSkill(skillSnapshot[i]);
                    stored++;
                }
            }

            return stored;
        }

        /// <summary>胜者取走池中第一张特质卡。</summary>
        public bool TryTakeTrait(out TraitCard card)
        {
            return TryTakeTraitAt(0, out card);
        }

        public bool TryTakeTraitAt(int index, out TraitCard card)
        {
            if (index < 0 || index >= traits.Count)
            {
                card = default;
                return false;
            }

            card = traits[index];
            traits.RemoveAt(index);
            return true;
        }

        public bool TryTakeSkill(out SkillCard card)
        {
            return TryTakeSkillAt(0, out card);
        }

        public bool TryTakeSkillAt(int index, out SkillCard card)
        {
            if (index < 0 || index >= skills.Count)
            {
                card = default;
                return false;
            }

            card = skills[index];
            skills.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// B11 的完整流程：胜者挑选一张带走 + 一张被淘汰者的牌离开组件池。
        /// 返回 false 表示池里已没有可取的内容（调用方应按「放弃夺取」结算回血）。
        /// </summary>
        public bool TryGrantOneTo(IBellParticipant winner, bool preferTraitFirst)
        {
            if (winner == null)
            {
                return false;
            }

            if (preferTraitFirst)
            {
                TraitCard traitCard;
                if (TryTakeTrait(out traitCard))
                {
                    if (winner.AddTrait(traitCard))
                    {
                        return true;
                    }

                    // 胜者已持有同款特质时把牌放回去，继续尝试技能卡。
                    AddTrait(traitCard);
                }

                SkillCard skillCard;
                if (TryTakeSkill(out skillCard) && winner.AddSkill(skillCard))
                {
                    return true;
                }
            }
            else
            {
                SkillCard skillCard;
                if (TryTakeSkill(out skillCard))
                {
                    if (winner.AddSkill(skillCard))
                    {
                        return true;
                    }

                    AddSkill(skillCard);
                }

                TraitCard traitCard;
                if (TryTakeTrait(out traitCard) && winner.AddTrait(traitCard))
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            traits.Clear();
            skills.Clear();
        }

        public string Describe()
        {
            var parts = new List<string>(traits.Count + skills.Count + 1);
            for (int i = 0; i < traits.Count; i++)
            {
                parts.Add(traits[i].ToDisplayName());
            }

            for (int i = 0; i < skills.Count; i++)
            {
                parts.Add(skills[i].ToDisplayName());
            }

            return parts.Count == 0 ? "组件池为空。" : "组件池：" + string.Join("、", parts);
        }

        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            if (new HashSet<TraitCard>(traits).Count != traits.Count)
            {
                report.Error("BellComponentPool", "组件池出现重复特质卡。");
            }

            if (new HashSet<SkillCard>(skills).Count != skills.Count)
            {
                report.Error("BellComponentPool", "组件池出现重复技能卡。");
            }
        }
    }
}
