using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>
    /// B1 位置钟声：场上剩余的人位置互换（= 重新匹配对手）。
    /// B8：普通阶段重新匹配后仍是 3 场 1v1。
    /// B12：淘汰制下若剩余人数为奇数，落单者与自己的复制体对决
    ///      （复制体生命与防御比本体低 20%，由上层按 BellCloneRequest 生成）。
    /// B13 只豁免特质与技能，所以集齐者仍参与位置互换。
    /// </summary>
    public sealed class PositionBellEffect : BellEffectBase
    {
        public override BellKind Kind
        {
            get { return BellKind.Position; }
        }

        public override BellResult Execute(BellContext context)
        {
            IReadOnlyList<IBellParticipant> inPlay = context.Registry.GetInPlay();
            var targets = new List<IBellParticipant>(inPlay.Count);
            var excluded = new List<int>();
            for (int i = 0; i < inPlay.Count; i++)
            {
                IBellParticipant p = inPlay[i];
                if (IsExcludedFromPositionSwap(context, p.Id))
                {
                    excluded.Add(p.Id);
                    continue;
                }

                targets.Add(p);
            }

            if (targets.Count < 2)
            {
                return BellResult.CreateNotExecuted(
                    context.Kind, context.Trigger, context.Phase, context.SequenceNumber,
                    "在场人数不足 2 人，位置钟声不执行。");
            }

            var result = CreateResult(context);

            var ids = new List<int>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                ids.Add(targets[i].Id);
            }

            // 打乱后的两两配对就是本次的位置互换（重新匹配对手）。
            IReadOnlyList<BellMatchPair> pairs = BellPairingUtility.PairShuffled(ids, context.Random);
            for (int i = 0; i < pairs.Count; i++)
            {
                result.Swaps.Add(new BellPositionSwap(pairs[i].FirstId, pairs[i].SecondId));
            }

            bool leftover = BellPairingUtility.HasLeftover(targets.Count);
            if (leftover)
            {
                int leftoverId = -1;
                // 找出没有出现在任何一对里的人。
                for (int i = 0; i < ids.Count; i++)
                {
                    bool paired = false;
                    for (int j = 0; j < pairs.Count; j++)
                    {
                        if (pairs[j].FirstId == ids[i] || pairs[j].SecondId == ids[i])
                        {
                            paired = true;
                            break;
                        }
                    }

                    if (!paired)
                    {
                        leftoverId = ids[i];
                        break;
                    }
                }

                if (context.Phase == BellPhase.Elimination)
                {
                    // B12：与自己的复制体对决。
                    result.Clone = new BellCloneRequest(
                        leftoverId,
                        context.Rules.CloneHealthMultiplier,
                        context.Rules.CloneDefenseMultiplier);
                    result.AddNote("淘汰制人数为奇数：落单者 " + leftoverId + " 生成复制体对决（B12）。");
                }
                else
                {
                    result.AddNote("普通阶段人数为奇数：落单者 " + leftoverId + " 本轮轮空（B8 期望 6 人 = 3 场 1v1）。");
                }
            }

            if (excluded.Count > 0)
            {
                result.AddNote("B7 限制：追踪双方不参与本次位置互换 → " + string.Join(", ", excluded));
            }

            if (context.Phase == BellPhase.Normal && targets.Count != context.Rules.MatchCount * 2)
            {
                result.AddNote("当前在场 " + targets.Count + " 人，而普通阶段期望 "
                    + (context.Rules.MatchCount * 2) + " 人（3 场 1v1）。");
            }

            RecordHoldings(result, targets);
            result.Summary = "位置互换 " + pairs.Count + " 组，重新匹配对手。";
            return result;
        }
    }
}
