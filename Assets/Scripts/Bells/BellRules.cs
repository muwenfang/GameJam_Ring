using Ring.Core;

namespace Ring.Bells
{
    /// <summary>
    /// 钟声系统的可调数值。默认值全部来自文档条款，改这里等于改规则，
    /// 因此既可以被 BellSettings（ScriptableObject）覆盖，也可以在离线测试里直接构造。
    /// </summary>
    public sealed class BellRules
    {
        /// <summary>B4：无人集齐人设时，每隔 30 秒自动触发一次钟声。</summary>
        public const float DefaultIntervalSeconds = 30f;

        /// <summary>B8：被杀方重新分配到 6 个区域之一。</summary>
        public const int DefaultRegionCount = 6;

        /// <summary>B7：追踪持续保持两次钟声。</summary>
        public const int DefaultTrackingDurationBells = 2;

        /// <summary>B12：复制体的生命与防御比本体低 20%（即 ×0.8）。</summary>
        public const float DefaultCloneMultiplier = 0.8f;

        /// <summary>B11：淘汰时若选择放弃夺取，恢复 20% 生命。</summary>
        public const float DefaultEliminationHealPercent = 0.2f;

        /// <summary>B8：普通阶段固定 3 场 1v1。</summary>
        public const int DefaultMatchCount = 3;

        /// <summary>两次钟声之间的间隔（秒），B4 的 30 秒。</summary>
        public float IntervalSeconds = DefaultIntervalSeconds;

        /// <summary>地图区域数量，B8 的 6 区域。</summary>
        public int RegionCount = DefaultRegionCount;

        /// <summary>追踪持续多少次钟声，B7 的 2 次。</summary>
        public int TrackingDurationBells = DefaultTrackingDurationBells;

        /// <summary>B7 两种读法的开关，默认 SelectionOnly（见 BellTrackingExclusion）。</summary>
        public BellTrackingExclusion TrackingExclusion = BellTrackingExclusion.SelectionOnly;

        /// <summary>B12 复制体的生命倍率。</summary>
        public float CloneHealthMultiplier = DefaultCloneMultiplier;

        /// <summary>B12 复制体的防御倍率。</summary>
        public float CloneDefenseMultiplier = DefaultCloneMultiplier;

        /// <summary>B11 放弃夺取时的回血比例（满血的 20%）。</summary>
        public float EliminationHealPercent = DefaultEliminationHealPercent;

        /// <summary>普通阶段每场 1v1 的对数，B8 的 3 场。</summary>
        public int MatchCount = DefaultMatchCount;

        /// <summary>
        /// 特质/技能钟声在「各人持有数量不等」（例如 B6 夺取后）时如何处理：
        /// true = 按各自原持有数量守恒分配（不丢牌、不凭空生成）；
        /// false = 所有人重新平均为 1 个特质 / 每级 1 个技能。
        /// 文档只描述了标准状态（1 特质 + 3 技能），故默认守恒。
        /// </summary>
        public bool PreserveCardCounts = true;

        /// <summary>把另一份规则逐字段覆盖到本方（用于运行时改规则而不换对象）。</summary>
        public void CopyFrom(BellRules other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            IntervalSeconds = other.IntervalSeconds;
            RegionCount = other.RegionCount;
            TrackingDurationBells = other.TrackingDurationBells;
            TrackingExclusion = other.TrackingExclusion;
            CloneHealthMultiplier = other.CloneHealthMultiplier;
            CloneDefenseMultiplier = other.CloneDefenseMultiplier;
            EliminationHealPercent = other.EliminationHealPercent;
            MatchCount = other.MatchCount;
            PreserveCardCounts = other.PreserveCardCounts;
        }

        public BellRules Clone()
        {
            var copy = new BellRules();
            copy.CopyFrom(this);
            return copy;
        }

        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            if (IntervalSeconds <= 0f)
            {
                report.Error("BellRules.IntervalSeconds", "钟声间隔必须大于 0，当前为 " + IntervalSeconds);
            }

            if (RegionCount < 2)
            {
                report.Warn("BellRules.RegionCount", "区域数量小于 2，位置钟声没有意义。");
            }

            if (TrackingDurationBells < 1)
            {
                report.Warn("BellRules.TrackingDurationBells", "追踪持续时间小于 1 次钟声，追踪将立刻失效。");
            }

            if (CloneHealthMultiplier <= 0f || CloneHealthMultiplier >= 1f)
            {
                report.Warn("BellRules.CloneHealthMultiplier", "B12 复制体生命倍率通常应在 (0,1) 之间，当前为 " + CloneHealthMultiplier);
            }

            if (EliminationHealPercent < 0f || EliminationHealPercent > 1f)
            {
                report.Warn("BellRules.EliminationHealPercent", "B11 回血比例应在 [0,1] 之间，当前为 " + EliminationHealPercent);
            }
        }
    }
}
