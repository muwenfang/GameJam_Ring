using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>一组 1v1 配对。</summary>
    public sealed class BellMatchPair
    {
        public BellMatchPair(int firstId, int secondId)
        {
            FirstId = firstId;
            SecondId = secondId;
        }

        public int FirstId { get; }

        public int SecondId { get; }

        public override string ToString()
        {
            return FirstId + " vs " + SecondId;
        }
    }

    /// <summary>
    /// 配对工具。B8「仍 3 场 1v1」与 B12「落单者与复制体对决」都建立在这上面。
    /// 只做纯计算，不涉及位置/场景。
    /// </summary>
    public static class BellPairingUtility
    {
        /// <summary>按给定顺序两两配对：0-1、2-3、……（不改变输入）。</summary>
        public static IReadOnlyList<BellMatchPair> PairInOrder(IReadOnlyList<int> ids)
        {
            var pairs = new List<BellMatchPair>(ids == null ? 0 : ids.Count / 2);
            if (ids == null)
            {
                return pairs;
            }

            for (int i = 0; i + 1 < ids.Count; i += 2)
            {
                pairs.Add(new BellMatchPair(ids[i], ids[i + 1]));
            }

            return pairs;
        }

        /// <summary>打乱后再两两配对（位置钟声用它重新匹配对手）。</summary>
        public static IReadOnlyList<BellMatchPair> PairShuffled(IReadOnlyList<int> ids, IBellRandom random)
        {
            var copy = new List<int>(ids == null ? 0 : ids.Count);
            if (ids != null)
            {
                copy.AddRange(ids);
            }

            if (random != null)
            {
                random.Shuffle(copy);
            }

            return PairInOrder(copy);
        }

        /// <summary>按参与者注册顺序取 ID 再打乱配对。</summary>
        public static IReadOnlyList<BellMatchPair> PairShuffled(IReadOnlyList<IBellParticipant> participants, IBellRandom random)
        {
            var ids = new List<int>(participants == null ? 0 : participants.Count);
            if (participants != null)
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    ids.Add(participants[i].Id);
                }
            }

            return PairShuffled(ids, random);
        }

        /// <summary>落单的人（人数为奇数时的最后一个）；偶数时为 -1。</summary>
        public static int FindLeftoverId(IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count == 0 || (ids.Count % 2) == 0)
            {
                return -1;
            }

            return ids[ids.Count - 1];
        }

        public static bool HasLeftover(int participantCount)
        {
            return participantCount > 0 && (participantCount % 2) == 1;
        }
    }
}
