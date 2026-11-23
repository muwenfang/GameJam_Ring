using System;
using System.Collections.Generic;
using Ring.Bells;

namespace Ring.Abilities
{
    /// <summary>
    /// 一个「能力」在整套人设里的槽位。文档《技能表.txt》把每个人设拆成五个槽:
    ///   特质（被动）、普攻、本我、自我、超我。
    ///
    /// 关键设定：这五个槽是**可以装在不同角色身上**的——角色 A 可以拿剑客的特质、
    /// 力士的普攻、钟表师的本我。因此「能力」的身份是 (人设, 槽位)，而不是「角色会什么」。
    /// 钟声系统只负责三张技能卡（本我/自我/超我）与特质卡，普攻卡由生成时发放，
    /// 两者最终都落到 <see cref="AbilityLoadout"/> 的五个槽里。
    ///
    /// 枚举顺序即 UI 顺序（特质→普攻→本我→自我→超我），数值不要重排。
    /// </summary>
    public enum AbilitySlot
    {
        /// <summary>特质（常驻被动）。</summary>
        Trait = 0,

        /// <summary>普攻（普通攻击的动作与参数）。</summary>
        BasicAttack = 1,

        /// <summary>本我技能。</summary>
        Id = 2,

        /// <summary>自我技能。</summary>
        Ego = 3,

        /// <summary>超我技能。</summary>
        Superego = 4,
    }

    /// <summary>槽位相关的纯函数工具与中英文对照。</summary>
    public static class AbilitySlots
    {
        /// <summary>一个人设共有 5 个槽位。</summary>
        public const int Count = 5;

        private static readonly AbilitySlot[] AllSlots =
        {
            AbilitySlot.Trait,
            AbilitySlot.BasicAttack,
            AbilitySlot.Id,
            AbilitySlot.Ego,
            AbilitySlot.Superego,
        };

        /// <summary>按 UI 顺序排列的 5 个槽位。</summary>
        public static IReadOnlyList<AbilitySlot> All
        {
            get { return AllSlots; }
        }

        /// <summary>是否是合法的槽位值（防序列化/反序列化越界）。</summary>
        public static bool IsDefined(this AbilitySlot slot)
        {
            int value = (int)slot;
            return value >= 0 && value < Count;
        }

        /// <summary>是否是技能槽（本我/自我/超我）——钟声「技能钟声」只碰这三个。</summary>
        public static bool IsSkill(this AbilitySlot slot)
        {
            return slot == AbilitySlot.Id || slot == AbilitySlot.Ego || slot == AbilitySlot.Superego;
        }

        /// <summary>是否是特质槽（常驻被动）。</summary>
        public static bool IsPassive(this AbilitySlot slot)
        {
            return slot == AbilitySlot.Trait;
        }

        /// <summary>是否是普攻槽。</summary>
        public static bool IsBasicAttack(this AbilitySlot slot)
        {
            return slot == AbilitySlot.BasicAttack;
        }

        /// <summary>技能槽对应的钟声等级；特质/普攻没有等级，返回本我作为兜底。</summary>
        public static SkillTier ToSkillTier(this AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Ego:
                    return SkillTier.Ego;
                case AbilitySlot.Superego:
                    return SkillTier.Superego;
                default:
                    return SkillTier.Id;
            }
        }

        /// <summary>钟声等级对应的技能槽（本我/自我/超我）。</summary>
        public static AbilitySlot FromSkillTier(SkillTier tier)
        {
            switch (tier)
            {
                case SkillTier.Ego:
                    return AbilitySlot.Ego;
                case SkillTier.Superego:
                    return AbilitySlot.Superego;
                default:
                    return AbilitySlot.Id;
            }
        }

        /// <summary>该槽位是否对应钟声的「技能钟声」（本我/自我/超我）。</summary>
        public static bool HasSkillTier(this AbilitySlot slot)
        {
            return slot.IsSkill();
        }

        /// <summary>中文名：特质 / 普攻 / 本我 / 自我 / 超我。</summary>
        public static string ToDisplayName(this AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Trait: return "特质";
                case AbilitySlot.BasicAttack: return "普攻";
                case AbilitySlot.Id: return "本我";
                case AbilitySlot.Ego: return "自我";
                case AbilitySlot.Superego: return "超我";
                default: return "槽位" + (int)slot;
            }
        }

        /// <summary>带括号的中文说明，用于 UI 副标题。</summary>
        public static string ToLongName(this AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Trait: return "特质（常驻被动）";
                case AbilitySlot.BasicAttack: return "普攻（基础攻击）";
                case AbilitySlot.Id: return "本我技能";
                case AbilitySlot.Ego: return "自我技能";
                case AbilitySlot.Superego: return "超我技能";
                default: return slot.ToDisplayName();
            }
        }

        /// <summary>代码里用的英文短名（日志/存档键）。</summary>
        public static string ToCodeName(this AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Trait: return "Trait";
                case AbilitySlot.BasicAttack: return "BasicAttack";
                case AbilitySlot.Id: return "Id";
                case AbilitySlot.Ego: return "Ego";
                case AbilitySlot.Superego: return "Superego";
                default: return "Slot" + (int)slot;
            }
        }

        /// <summary>按代码短名解析，解析不了返回 false。存档格式用得到。</summary>
        public static bool TryParse(string codeName, out AbilitySlot slot)
        {
            slot = AbilitySlot.Trait;
            if (string.IsNullOrWhiteSpace(codeName))
            {
                return false;
            }

            for (int i = 0; i < AllSlots.Length; i++)
            {
                if (string.Equals(AllSlots[i].ToCodeName(), codeName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    slot = AllSlots[i];
                    return true;
                }
            }

            return false;
        }
    }
}
