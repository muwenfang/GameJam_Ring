using System.Collections.Generic;
using Ring.Core;

namespace Ring.Bells
{
    /// <summary>一条追踪记录：谁在追谁、还能持续几次钟声（B7）。</summary>
    public sealed class TrackingEntry
    {
        public TrackingEntry(int trackerId, int targetId, int bellsRemaining)
        {
            TrackerId = trackerId;
            TargetId = targetId;
            BellsRemaining = bellsRemaining;
        }

        public int TrackerId { get; }

        public int TargetId { get; }

        /// <summary>剩余钟声次数；递减到 0 即失效。</summary>
        public int BellsRemaining { get; internal set; }

        public string Describe()
        {
            return "追踪：" + TrackerId + " → " + TargetId + "（剩 " + BellsRemaining + " 次钟声）";
        }

        public override string ToString()
        {
            return Describe();
        }
    }

    /// <summary>
    /// 追踪账本。负责 B7 的三件事：
    /// 1）追踪持续保持两次钟声（次数由 BellRules.TrackingDurationBells 给出）；
    /// 2）每次钟声结算后递减；
    /// 3）若下次钟声为人为触发，追踪者与被追踪者受到额外限制。
    /// </summary>
    public sealed class BellTrackingLedger
    {
        private readonly List<TrackingEntry> entries = new List<TrackingEntry>(4);

        public IReadOnlyList<TrackingEntry> Entries
        {
            get { return entries; }
        }

        public int Count
        {
            get { return entries.Count; }
        }

        public bool IsTracking(int trackerId)
        {
            TrackingEntry ignored;
            return TryGetEntry(trackerId, out ignored);
        }

        public bool TryGetEntry(int trackerId, out TrackingEntry entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].TrackerId == trackerId)
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>追踪目标 ID；没有追踪时返回 -1。</summary>
        public int GetTargetId(int trackerId)
        {
            TrackingEntry entry;
            return TryGetEntry(trackerId, out entry) ? entry.TargetId : -1;
        }

        /// <summary>
        /// 开始追踪（B6 击杀后选择追踪）。同一追踪者只能同时追一个人，重复调用会覆盖。
        /// 自己追自己、ID 相同都视为非法并返回 false。
        /// </summary>
        public bool BeginTracking(int trackerId, int targetId, int durationBells)
        {
            if (trackerId == targetId || durationBells < 1)
            {
                return false;
            }

            CancelTracking(trackerId);
            entries.Add(new TrackingEntry(trackerId, targetId, durationBells));
            return true;
        }

        public bool CancelTracking(int trackerId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].TrackerId == trackerId)
                {
                    entries.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 每次钟声结算后调用：所有追踪次数 -1，归零的移除。
        /// 注意应在效果执行完之后调用（效果要靠本次的追踪状态做 B7 判定）。
        /// </summary>
        public void OnBellResolved()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                TrackingEntry entry = entries[i];
                entry.BellsRemaining -= 1;
                if (entry.BellsRemaining <= 0)
                {
                    entries.RemoveAt(i);
                }
            }
        }

        /// <summary>该 ID 是否在任意一条追踪记录的任一端（追踪者或被追踪者）。</summary>
        public bool IsTrackingParty(int participantId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].TrackerId == participantId || entries[i].TargetId == participantId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// B7 默认读法：下一次钟声若为人为触发，追踪双方不能被选为新的追踪对象。
        /// </summary>
        public bool IsExcludedFromTrackingSelection(
            int participantId,
            BellTrigger upcomingTrigger,
            BellTrackingExclusion mode)
        {
            if (mode != BellTrackingExclusion.SelectionOnly || !upcomingTrigger.IsHumanInitiated())
            {
                return false;
            }

            return IsTrackingParty(participantId);
        }

        /// <summary>B7 备选读法：下一次钟声若为人为触发，追踪双方不参与位置交换。</summary>
        public bool IsExcludedFromPositionSwap(
            int participantId,
            BellTrigger upcomingTrigger,
            BellTrackingExclusion mode)
        {
            if (mode != BellTrackingExclusion.SelectionAndPosition || !upcomingTrigger.IsHumanInitiated())
            {
                return false;
            }

            return IsTrackingParty(participantId);
        }

        public void Clear()
        {
            entries.Clear();
        }

        public string Describe()
        {
            if (entries.Count == 0)
            {
                return "当前没有追踪关系。";
            }

            var lines = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                lines.Add(entries[i].Describe());
            }

            return string.Join("\n", lines);
        }

        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                TrackingEntry entry = entries[i];
                if (entry.TrackerId == entry.TargetId)
                {
                    report.Error("BellTracking", "出现自己追自己的记录：" + entry.TrackerId);
                }

                if (entry.BellsRemaining < 1)
                {
                    report.Error("BellTracking", "追踪剩余次数异常：" + entry.Describe());
                }
            }
        }
    }
}
