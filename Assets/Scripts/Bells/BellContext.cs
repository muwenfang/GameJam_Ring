namespace Ring.Bells
{
    /// <summary>
    /// 一次钟声执行时传给效果的全部信息。效果只读这里，绝不自己去找全局状态，
    /// 这样同一个效果可以在离线测试里被任意构造的上下文驱动。
    /// </summary>
    public sealed class BellContext
    {
        public BellContext(
            BellKind kind,
            BellTrigger trigger,
            BellPhase phase,
            int sequenceNumber,
            BellParticipantRegistry registry,
            IBellRandom random,
            BellTrackingLedger tracking,
            BellComponentPool pool,
            BellRules rules)
        {
            Kind = kind;
            Trigger = trigger;
            Phase = phase;
            SequenceNumber = sequenceNumber;
            Registry = registry;
            Random = random;
            Tracking = tracking;
            Pool = pool;
            Rules = rules;
        }

        public BellKind Kind { get; }

        public BellTrigger Trigger { get; }

        public BellPhase Phase { get; }

        /// <summary>这是本局第几次钟声（从 1 开始）。</summary>
        public int SequenceNumber { get; }

        public BellParticipantRegistry Registry { get; }

        public IBellRandom Random { get; }

        public BellTrackingLedger Tracking { get; }

        public BellComponentPool Pool { get; }

        public BellRules Rules { get; }

        /// <summary>B7：本次钟声是否为「人为触发」，是则追踪双方受到额外限制。</summary>
        public bool IsHumanInitiated
        {
            get { return Trigger.IsHumanInitiated(); }
        }
    }
}
