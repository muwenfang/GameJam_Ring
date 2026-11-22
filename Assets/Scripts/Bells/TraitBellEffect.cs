using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>
    /// B1 特质钟声：收集场上剩余的特质 → 打乱 → 重新分配给场上所有人。
    /// B13：已集齐完整人设的角色豁免，既不被收走也不参与分配。
    /// 若各人持有数量不等（B6 夺取之后），按持有量守恒分配（BellRules.PreserveCardCounts）。
    /// </summary>
    public sealed class TraitBellEffect : BellEffectBase
    {
        public override BellKind Kind
        {
            get { return BellKind.Trait; }
        }

        public override bool CanExecute(BellContext context)
        {
            if (!base.CanExecute(context))
            {
                return false;
            }

            return CountRedistributableTraits(context.Registry.GetRedistributable()) > 0;
        }

        public override BellResult Execute(BellContext context)
        {
            IReadOnlyList<IBellParticipant> targets = context.Registry.GetRedistributable();
            if (!CanExecute(context))
            {
                return BellResult.CreateNotExecuted(
                    context.Kind, context.Trigger, context.Phase, context.SequenceNumber,
                    "场上没有可重新分配的特质（可能都已集齐或都已收走）。");
            }

            // 1) 记录每人的持有量并收集全部特质卡。
            var counts = new int[targets.Count];
            var pool = new List<TraitCard>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                IReadOnlyList<TraitCard> held = targets[i].Traits;
                counts[i] = held.Count;
                for (int j = 0; j < held.Count; j++)
                {
                    pool.Add(held[j]);
                }
            }

            // 2) 从各自身上取下来，避免重复持有。
            for (int i = 0; i < targets.Count; i++)
            {
                IBellParticipant p = targets[i];
                for (int j = 0; j < counts[i]; j++)
                {
                    // 每轮取第一张，保证一定取干净（counts 已经是本次快照的数量）。
                    if (p.Traits.Count > 0)
                    {
                        p.RemoveTrait(p.Traits[0]);
                    }
                }
            }

            // 3) 打乱后按原持有量重新发牌。
            context.Random.Shuffle(pool);
            var result = CreateResult(context);
            var unassigned = new List<TraitCard>();
            int index = 0;
            int dealt = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                int amount = context.Rules.PreserveCardCounts ? counts[i] : (i == 0 ? pool.Count : 0);
                for (int k = 0; k < amount && index < pool.Count; k++)
                {
                    TraitCard card = pool[index];
                    index++;
                    if (targets[i].AddTrait(card))
                    {
                        dealt++;
                    }
                    else
                    {
                        unassigned.Add(card);
                    }
                }
            }

            // 4) 分配失败的卡退回池子（正常情况下不会发生）。
            while (index < pool.Count)
            {
                unassigned.Add(pool[index]);
                index++;
            }

            for (int i = 0; i < unassigned.Count; i++)
            {
                context.Pool.AddTrait(unassigned[i]);
            }

            RecordHoldings(result, targets);
            result.Summary = "收回 " + pool.Count + " 张特质，打乱后重新分配给 " + dealt + " 张持有量。";
            if (unassigned.Count > 0)
            {
                result.AddNote("有 " + unassigned.Count + " 张特质无人可放，已放入组件池。");
            }

            if (!context.Rules.PreserveCardCounts)
            {
                result.AddNote("规则配置为「平均分配」，所有特质集中在首位持有者。");
            }

            return result;
        }

        private static int CountRedistributableTraits(IReadOnlyList<IBellParticipant> targets)
        {
            int count = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                count += targets[i].Traits.Count;
            }

            return count;
        }
    }
}
