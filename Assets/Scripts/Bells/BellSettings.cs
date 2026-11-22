using Ring.Core;
using UnityEngine;

namespace Ring.Bells
{
    /// <summary>
    /// 钟声规则资产：把文档里的可调数值暴露到 Inspector，避免硬编码。
    /// 运行时由 BellSystem 读一次并转成纯 C# 的 BellRules 交给 BellRuntime，
    /// 所以改这里等于改一局的规则，纯逻辑层不需要知道 ScriptableObject。
    /// </summary>
    [CreateAssetMenu(fileName = "BellSettings", menuName = "钟声/钟声规则", order = 20)]
    public sealed class BellSettings : ScriptableObject
    {
        [Header("B4 定时钟声")]
        [Tooltip("无人集齐人设时，每隔多少秒自动触发一次钟声。文档为 30 秒。")]
        [Min(1f)]
        public float intervalSeconds = BellRules.DefaultIntervalSeconds;

        [Header("B8 普通阶段")]
        [Tooltip("地图区域数量，被杀方会被重新分配到其中之一。文档为 6 个区域。")]
        [Min(2)]
        public int regionCount = BellRules.DefaultRegionCount;

        [Tooltip("普通阶段每场 1v1 的对数，文档为 3 场（6 人）。")]
        [Min(1)]
        public int matchCount = BellRules.DefaultMatchCount;

        [Header("B7 追踪")]
        [Tooltip("追踪持续多少次钟声。文档为两次。")]
        [Min(1)]
        public int trackingDurationBells = BellRules.DefaultTrackingDurationBells;

        [Tooltip("「追踪者与被追踪者不能成为追踪对象」的两种读法，默认只排除新的追踪目标。")]
        public BellTrackingExclusion trackingExclusion = BellTrackingExclusion.SelectionOnly;

        [Header("B12 复制体")]
        [Tooltip("淘汰制下人数为奇数时，落单者复制体的生命倍率。文档为低 20%（0.8）。")]
        [Range(0.1f, 1f)]
        public float cloneHealthMultiplier = BellRules.DefaultCloneMultiplier;

        [Tooltip("落单者复制体的防御倍率。文档为低 20%（0.8）。")]
        [Range(0.1f, 1f)]
        public float cloneDefenseMultiplier = BellRules.DefaultCloneMultiplier;

        [Header("B11 淘汰结算")]
        [Tooltip("淘汰时放弃夺取组件池中的卡牌，可恢复的生命比例（满血为 1）。文档为 20%。")]
        [Range(0f, 1f)]
        public float eliminationHealPercent = BellRules.DefaultEliminationHealPercent;

        [Header("分配策略")]
        [Tooltip("各人持有数量不等时（B6 夺取之后），是否按原持有量守恒分配。关闭则把牌集中到一个人身上。")]
        public bool preserveCardCounts = true;

        [TextArea(3, 8)]
        [Tooltip("设计备注：改动原因、平衡性记录等。")]
        public string designNotes =
            "数值全部来自文档：30 秒定时、6 区域、追踪两次、复制体低 20%、放弃夺取回血 20%。"
            + "若后续需要「钟声种类权重」或「特殊钟声」，在这里加字段并在 BellRuntime.RingRandomKind 里消费。";

        /// <summary>转成纯逻辑层的规则对象（每次调用产生副本，避免运行时改到资产）。</summary>
        public BellRules ToRules()
        {
            var rules = new BellRules
            {
                IntervalSeconds = intervalSeconds,
                RegionCount = regionCount,
                MatchCount = matchCount,
                TrackingDurationBells = trackingDurationBells,
                TrackingExclusion = trackingExclusion,
                CloneHealthMultiplier = cloneHealthMultiplier,
                CloneDefenseMultiplier = cloneDefenseMultiplier,
                EliminationHealPercent = eliminationHealPercent,
                PreserveCardCounts = preserveCardCounts,
            };
            return rules;
        }

        /// <summary>把一份规则写回资产（用于从代码/测试批量配置）。</summary>
        public void CopyFrom(BellRules rules)
        {
            if (rules == null)
            {
                return;
            }

            intervalSeconds = rules.IntervalSeconds;
            regionCount = rules.RegionCount;
            matchCount = rules.MatchCount;
            trackingDurationBells = rules.TrackingDurationBells;
            trackingExclusion = rules.TrackingExclusion;
            cloneHealthMultiplier = rules.CloneHealthMultiplier;
            cloneDefenseMultiplier = rules.CloneDefenseMultiplier;
            eliminationHealPercent = rules.EliminationHealPercent;
            preserveCardCounts = rules.PreserveCardCounts;
        }

        [ContextMenu("恢复文档默认值")]
        public void ResetToDocumentDefaults()
        {
            CopyFrom(new BellRules());
        }

        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            ToRules().CollectIssues(report);
        }

        public string ToSummary()
        {
            return "钟声规则：间隔 " + intervalSeconds + "s / 区域 " + regionCount
                + " / 普通阶段 " + matchCount + " 场 1v1 / 追踪 " + trackingDurationBells + " 次钟声"
                + " / 复制体 ×" + cloneHealthMultiplier + " / 放弃夺取回血 " + (eliminationHealPercent * 100f) + "%";
        }
    }
}
