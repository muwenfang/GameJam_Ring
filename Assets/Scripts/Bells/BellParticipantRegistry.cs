using System.Collections.Generic;
using Ring.Core;

namespace Ring.Bells
{
    /// <summary>
    /// 场上参与者名册。钟声的所有效果都只通过这里取人，不直接持有角色对象，
    /// 这样效果逻辑可以离线测试，也可以被 UI / 结算面板复用。
    /// </summary>
    public sealed class BellParticipantRegistry
    {
        private readonly List<IBellParticipant> participants = new List<IBellParticipant>(8);

        public IReadOnlyList<IBellParticipant> All
        {
            get { return participants; }
        }

        public int Count
        {
            get { return participants.Count; }
        }

        /// <summary>还在场上、参与钟声的人数。</summary>
        public int InPlayCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < participants.Count; i++)
                {
                    if (participants[i].IsInPlay)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool TryAdd(IBellParticipant participant, out string error)
        {
            error = null;
            if (participant == null)
            {
                error = "参与者为空。";
                return false;
            }

            if (Get(participant.Id) != null)
            {
                error = "参与者 ID 重复：" + participant.Id;
                return false;
            }

            participants.Add(participant);
            return true;
        }

        public bool Add(IBellParticipant participant)
        {
            string ignored;
            return TryAdd(participant, out ignored);
        }

        public bool Remove(int participantId)
        {
            for (int i = 0; i < participants.Count; i++)
            {
                if (participants[i].Id == participantId)
                {
                    participants.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>找不到时返回 null（Unity 的 C# 版本未开启可空引用类型注解）。</summary>
        public IBellParticipant Get(int participantId)
        {
            for (int i = 0; i < participants.Count; i++)
            {
                if (participants[i].Id == participantId)
                {
                    return participants[i];
                }
            }

            return null;
        }

        /// <summary>按注册顺序返回在场者（顺序稳定，配对与日志都依赖它）。</summary>
        public IReadOnlyList<IBellParticipant> GetInPlay()
        {
            var result = new List<IBellParticipant>(participants.Count);
            for (int i = 0; i < participants.Count; i++)
            {
                if (participants[i].IsInPlay)
                {
                    result.Add(participants[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 在场且未免疫（B13）的人 —— 特质/技能钟声的分配对象。
        /// 豁免判定以「是否集齐一套」为准，同时兼容角色层手动置的免疫标记：
        /// 集齐是文档 [016] 的硬条件，标记只是给外部（UI/其他系统）读的快照。
        /// </summary>
        public IReadOnlyList<IBellParticipant> GetRedistributable()
        {
            var result = new List<IBellParticipant>(participants.Count);
            for (int i = 0; i < participants.Count; i++)
            {
                IBellParticipant p = participants[i];
                if (p.IsInPlay && !p.IsImmuneToRedistribution
                    && !BellCards.HasCompleteSet(p.Traits, p.Skills))
                {
                    result.Add(p);
                }
            }

            return result;
        }

        /// <summary>第一个集齐完整人设的在场的角色；没有则返回 null。B9 用它切换淘汰制。</summary>
        public IBellParticipant FindFirstCompleteSet()
        {
            for (int i = 0; i < participants.Count; i++)
            {
                IBellParticipant p = participants[i];
                if (p.IsInPlay && BellCards.HasCompleteSet(p.Traits, p.Skills))
                {
                    return p;
                }
            }

            return null;
        }

        public bool AnyCompleteSet
        {
            get { return FindFirstCompleteSet() != null; }
        }

        public void Clear()
        {
            participants.Clear();
        }

        /// <summary>名册自身的结构性问题（重名 ID 等）。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            var seen = new HashSet<int>();
            for (int i = 0; i < participants.Count; i++)
            {
                IBellParticipant p = participants[i];
                if (p == null)
                {
                    report.Error("Participants", "名册中存在空引用。");
                    continue;
                }

                if (!seen.Add(p.Id))
                {
                    report.Error("Participants", "参与者 ID 重复：" + p.Id);
                }
            }
        }
    }
}
