using System;
using Ring.Characters;

namespace Ring.Bells
{
    /// <summary>
    /// 钟声种类。依据文档《数据和结构.txt》钟声条款 B1–B5：
    /// 场上任何一种钟声被敲响时，会同时进行该钟声对应的交换/重新匹配。
    /// 钟声种类只有三种，全流程共用一套规则。
    /// </summary>
    public enum BellKind
    {
        /// <summary>特质钟声（B1）：收集场上剩余特质 → 打乱 → 重新分配给场上所有人。</summary>
        Trait = 0,

        /// <summary>技能钟声（B1）：同理，但保证每个技能等级仍然各一个。</summary>
        Skill = 1,

        /// <summary>位置钟声（B1）：场上剩余的人位置互换（重新匹配对手）。</summary>
        Position = 2,
    }

    /// <summary>
    /// 钟声的触发来源。用来区分「定时自动」「死亡强制」「追踪强制」「手动调试」，
    /// 并决定 B7 中「人为触发」的额外限制是否生效。
    /// </summary>
    public enum BellTrigger
    {
        /// <summary>B4：无人集齐人设时，每隔固定时间自动触发一次，种类随机。</summary>
        Timer = 0,

        /// <summary>B3：出现死亡一定触发钟声，且必定为位置钟声。</summary>
        Death = 1,

        /// <summary>B5：击杀后选择跟踪时，本次钟声必定触发位置交换。</summary>
        Tracking = 2,

        /// <summary>调试、剧情或教学需要而手动敲响的钟声。</summary>
        Manual = 3,
    }

    /// <summary>
    /// 对局阶段。B8 普通阶段：场上 6 人 = 3 场 1v1，死亡方复活并重新分区；
    /// B9/B10 淘汰制：有人集齐完整人设后进入，死亡即淘汰，追踪变一次性。
    /// </summary>
    public enum BellPhase
    {
        /// <summary>普通阶段（B8）：3 场 1v1，死亡只重置，不淘汰。</summary>
        Normal = 0,

        /// <summary>淘汰制（B9/B10/B11/B12）：死亡即淘汰，卡牌进入组件池。</summary>
        Elimination = 1,
    }

    /// <summary>
    /// 技能等级。文档把每个技能分成 本我 / 自我 / 超我 三档，
    /// 一人每个等级各持一个（技能槽位 1/2/3 分别对应三个等级）。
    /// </summary>
    public enum SkillTier
    {
        /// <summary>本我（技能 1）。</summary>
        Id = 1,

        /// <summary>自我（技能 2）。</summary>
        Ego = 2,

        /// <summary>超我（技能 3）。</summary>
        Superego = 3,
    }

    /// <summary>
    /// B7 的第二种读法开关。
    /// 文档原文：「若下次钟声为人为触发，则追踪者与被追踪者不能成为追踪对象」。
    /// 句子可以读成两种意思，这里用枚举把两种实现都保留下来，默认取第一种。
    /// </summary>
    public enum BellTrackingExclusion
    {
        /// <summary>默认：追踪者与被追踪者不能被选为「新的追踪目标」（B6 的夺取/追踪选项）。</summary>
        SelectionOnly = 0,

        /// <summary>备选读法：追踪者与被追踪者本次钟声不参与位置交换（B12 的配对）。</summary>
        SelectionAndPosition = 1,
    }

    /// <summary>钟声枚举的中文显示与说明，供 UI / 日志 / 检查器共用。</summary>
    public static class BellLabels
    {
        public static string ToDisplayName(this BellKind kind)
        {
            switch (kind)
            {
                case BellKind.Trait: return "特质钟声";
                case BellKind.Skill: return "技能钟声";
                case BellKind.Position: return "位置钟声";
                default: return "未知钟声";
            }
        }

        public static string ToRuleDescription(this BellKind kind)
        {
            switch (kind)
            {
                case BellKind.Trait:
                    return "收集场上剩余的特质，打乱后重新分配给场上所有人（B1）。";
                case BellKind.Skill:
                    return "收集场上剩余的技能，打乱后重新分配，并保持每个等级各一个（B1）。";
                case BellKind.Position:
                    return "场上剩余的人位置互换，重新匹配对手；淘汰制下人数为奇数时，落单者与自己的复制体对决（B1/B12）。";
                default:
                    return string.Empty;
            }
        }

        public static string ToDisplayName(this BellTrigger trigger)
        {
            switch (trigger)
            {
                case BellTrigger.Timer: return "定时触发";
                case BellTrigger.Death: return "死亡强制触发";
                case BellTrigger.Tracking: return "追踪强制触发";
                case BellTrigger.Manual: return "手动触发";
                default: return "未知触发";
            }
        }

        /// <summary>B7 里的「人为触发」：由玩家选择/操作引起的钟声。</summary>
        public static bool IsHumanInitiated(this BellTrigger trigger)
        {
            return trigger == BellTrigger.Tracking || trigger == BellTrigger.Manual;
        }

        public static string ToDisplayName(this BellPhase phase)
        {
            return phase == BellPhase.Elimination ? "淘汰制" : "普通阶段";
        }

        public static string ToDisplayName(this SkillTier tier)
        {
            switch (tier)
            {
                case SkillTier.Id: return "本我";
                case SkillTier.Ego: return "自我";
                case SkillTier.Superego: return "超我";
                default: return "未知等级";
            }
        }

        public static string ToDisplayName(this PersonaId persona)
        {
            return PersonaIds.ToChineseName(persona);
        }
    }
}
