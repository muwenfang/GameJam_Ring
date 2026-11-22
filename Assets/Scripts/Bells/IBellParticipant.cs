using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>
    /// 钟声系统对「参与钟声的角色」的全部要求。
    /// 刻意保持极薄：只有身份、在场状态与卡牌读写。
    /// 真正的角色（生命、防御、位置、技能释放）不在这里实现 —— 技能与地图部分按用户要求暂不动，
    /// 角色脚本只要实现这个接口，就能接入钟声系统。
    /// </summary>
    public interface IBellParticipant
    {
        /// <summary>稳定 ID，同一局内不变（不要用 GetInstanceID，跨局/读档会变）。</summary>
        int Id { get; }

        /// <summary>日志与 UI 里显示的名字。</summary>
        string DisplayName { get; }

        /// <summary>
        /// 是否还在场上（未淘汰、未离场）。不在场者不参与任何钟声。
        /// 可写：淘汰（B11）由钟声系统负责让角色离场。
        /// </summary>
        bool IsInPlay { get; set; }

        /// <summary>
        /// 是否存活。可写：死亡事件由战斗层上报，钟声系统在 B3/B11 流程里负责落账。
        /// </summary>
        bool IsAlive { get; set; }

        /// <summary>
        /// B13：集齐完整人设的角色不会再被钟声随机分配技能和特质。
        /// 位置钟声仍会把他算进去（文档只写了「技能和特质」）。
        /// 可写：集齐/失去卡牌时由钟声系统维护这个标记。
        /// </summary>
        bool IsImmuneToRedistribution { get; set; }

        IReadOnlyList<TraitCard> Traits { get; }

        IReadOnlyList<SkillCard> Skills { get; }

        /// <summary>加入一张特质卡；已持有同名人设特质或不允许时返回 false。</summary>
        bool AddTrait(TraitCard card);

        bool RemoveTrait(TraitCard card);

        bool AddSkill(SkillCard card);

        bool RemoveSkill(SkillCard card);

        /// <summary>清空手里全部特质与技能（重开一局 / 重新发牌用）。</summary>
        void ClearCards();
    }
}
