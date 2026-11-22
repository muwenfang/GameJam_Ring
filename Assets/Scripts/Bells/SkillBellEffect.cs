using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>
    /// B1 技能钟声：与特质钟声同理，但「保持每种等级各一个」。
    /// 实现方式是按等级分别洗牌、分别发牌 —— 每个等级内部都是一次完整置换，
    /// 所以只要发牌前每个等级恰好 6 张（6 人设各 1），发牌后依然如此。
    /// B13 豁免者不参与，B6/B11 导致的持有量差异按量守恒。
    /// </summary>
    public sealed class SkillBellEffect : BellEffectBase
    {
        public override BellKind Kind
        {
            get { return BellKind.Skill; }
        }

        public override bool CanExecute(BellContext context)
        {
            if (!base.CanExecute(context))
            {
                return false;
            }

            return CountRedistributableSkills(context.Registry.GetRedistributable()) > 0;
        }

        public override BellResult Execute(BellContext context)
        {
            IReadOnlyList<IBellParticipant> targets = context.Registry.GetRedistributable();
            if (!CanExecute(context))
            {
                return BellResult.CreateNotExecuted(
                    context.Kind, context.Trigger, context.Phase, context.SequenceNumber,
                    "场上没有可重新分配的技能。");
            }

            var result = CreateResult(context);
            var unassigned = new List<SkillCard>();
            int moved = 0;

            for (int tierIndex = 0; tierIndex < BellCards.Tiers.Length; tierIndex++)
            {
                SkillTier tier = BellCards.Tiers[tierIndex];
                var pool = new List<SkillCard>(targets.Count);
                var snapshots = new List<List<SkillCard>>(targets.Count);

                // 1) 收集本等级的技能卡。
                for (int i = 0; i < targets.Count; i++)
                {
                    var held = new List<SkillCard>();
                    IReadOnlyList<SkillCard> skills = targets[i].Skills;
                    for (int j = 0; j < skills.Count; j++)
                    {
                        if (skills[j].Tier == tier)
                        {
                            held.Add(skills[j]);
                        }
                    }

                    snapshots.Add(held);
                    pool.AddRange(held);
                }

                if (pool.Count == 0)
                {
                    continue;
                }

                // 2) 取下来。
                for (int i = 0; i < targets.Count; i++)
                {
                    for (int j = 0; j < snapshots[i].Count; j++)
                    {
                        targets[i].RemoveSkill(snapshots[i][j]);
                    }
                }

                // 3) 该等级内部洗牌后按原持有量发回。
                context.Random.Shuffle(pool);
                int index = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    int amount = context.Rules.PreserveCardCounts ? snapshots[i].Count : (i == 0 ? pool.Count : 0);
                    for (int k = 0; k < amount && index < pool.Count; k++)
                    {
                        SkillCard card = pool[index];
                        index++;
                        if (targets[i].AddSkill(card))
                        {
                            moved++;
                        }
                        else
                        {
                            unassigned.Add(card);
                        }
                    }
                }

                while (index < pool.Count)
                {
                    unassigned.Add(pool[index]);
                    index++;
                }
            }

            for (int i = 0; i < unassigned.Count; i++)
            {
                context.Pool.AddSkill(unassigned[i]);
            }

            RecordHoldings(result, targets);
            result.Summary = "本我 / 自我 / 超我三档分别洗牌，共重新分配 " + moved + " 张技能卡。";
            if (unassigned.Count > 0)
            {
                result.AddNote("有 " + unassigned.Count + " 张技能卡无人可放，已放入组件池。");
            }

            return result;
        }

        private static int CountRedistributableSkills(IReadOnlyList<IBellParticipant> targets)
        {
            int count = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                count += targets[i].Skills.Count;
            }

            return count;
        }
    }
}
