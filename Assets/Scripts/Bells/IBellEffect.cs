using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>
    /// 一种钟声的执行逻辑。三种钟声（特质 / 技能 / 位置）各一个实现，
    /// 新增的钟声种类只需实现本接口并注册进 BellRuntime。
    /// </summary>
    public interface IBellEffect
    {
        BellKind Kind { get; }

        /// <summary>当前场上状态是否允许执行（例如位置钟声至少需要 2 人）。</summary>
        bool CanExecute(BellContext context);

        BellResult Execute(BellContext context);
    }

    /// <summary>三个效果共用的脚手架：结果对象创建、持有快照记录、排除过滤。</summary>
    public abstract class BellEffectBase : IBellEffect
    {
        public abstract BellKind Kind { get; }

        public virtual bool CanExecute(BellContext context)
        {
            return context != null && context.Registry != null && context.Registry.InPlayCount >= 2;
        }

        public abstract BellResult Execute(BellContext context);

        protected BellResult CreateResult(BellContext context)
        {
            return new BellResult(context.Kind, context.Trigger, context.Phase, context.SequenceNumber);
        }

        protected static void RecordHoldings(BellResult result, IReadOnlyList<IBellParticipant> subjects)
        {
            if (result == null || subjects == null)
            {
                return;
            }

            for (int i = 0; i < subjects.Count; i++)
            {
                IBellParticipant p = subjects[i];
                result.Assignments.Add(new BellAssignment(p.Id, p.DisplayName, p.Traits, p.Skills));
            }
        }

        /// <summary>B7：本次钟声为人为触发时，追踪双方不能成为新的追踪对象。</summary>
        protected static bool IsExcludedFromSelection(BellContext context, int participantId)
        {
            if (context.Tracking == null)
            {
                return false;
            }

            return context.Tracking.IsExcludedFromTrackingSelection(
                participantId, context.Trigger, context.Rules.TrackingExclusion);
        }

        /// <summary>B7 的备选读法：追踪双方不参与本次位置交换。</summary>
        protected static bool IsExcludedFromPositionSwap(BellContext context, int participantId)
        {
            if (context.Tracking == null)
            {
                return false;
            }

            return context.Tracking.IsExcludedFromPositionSwap(
                participantId, context.Trigger, context.Rules.TrackingExclusion);
        }
    }
}
