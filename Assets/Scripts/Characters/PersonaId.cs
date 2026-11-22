using System;
using System.Collections.Generic;

namespace Ring.Characters
{
    /// <summary>
    /// 六个人设的稳定标识。
    /// 顺序与设计文档《数据和结构》的表格一致；一旦有资产序列化了这些数值就不要调整顺序。
    /// </summary>
    public enum PersonaId
    {
        /// <summary>剑客：最高机动，高频连击。</summary>
        Swordsman = 0,

        /// <summary>蒸汽机械师：近身最高爆发，打完拉开。</summary>
        SteamMechanic = 1,

        /// <summary>斯巴达战士：狂战士 / 举盾坦。</summary>
        Spartan = 2,

        /// <summary>精灵游侠：射程 8 格最远，脆皮。</summary>
        Ranger = 3,

        /// <summary>钟表师：时间系，回溯 / 减速 / 致死撤销。</summary>
        Clockmaker = 4,

        /// <summary>力士：近战控制 / 击退 / 眩晕。</summary>
        Strongman = 5,
    }

    /// <summary>人设枚举的遍历与显示辅助。</summary>
    public static class PersonaIds
    {
        /// <summary>人设总数。</summary>
        public const int Count = 6;

        private static readonly PersonaId[] AllIds =
        {
            PersonaId.Swordsman,
            PersonaId.SteamMechanic,
            PersonaId.Spartan,
            PersonaId.Ranger,
            PersonaId.Clockmaker,
            PersonaId.Strongman,
        };

        private static readonly string[] ChineseNames =
        {
            "剑客",
            "蒸汽机械师",
            "斯巴达战士",
            "精灵游侠",
            "钟表师",
            "力士",
        };

        /// <summary>按文档顺序排列的全部人设。</summary>
        public static IReadOnlyList<PersonaId> All => AllIds;

        /// <summary>新手设配置资产时使用的默认中文名。</summary>
        public static string ToChineseName(this PersonaId persona)
        {
            int index = (int)persona;
            return index >= 0 && index < ChineseNames.Length ? ChineseNames[index] : persona.ToString();
        }

        /// <summary>判断数值是否为已定义的人设。</summary>
        public static bool IsDefined(PersonaId persona)
        {
            int index = (int)persona;
            return index >= 0 && index < Count;
        }

        /// <summary>枚举 Inspector 下拉框里用到的名字。</summary>
        public static string[] GetDisplayNames()
        {
            var names = new string[Count];
            for (int i = 0; i < Count; i++)
            {
                names[i] = ChineseNames[i];
            }

            return names;
        }

        /// <summary>用于资产命名/文件名的人设英文标识。</summary>
        public static string ToCodeName(this PersonaId persona)
        {
            return Enum.IsDefined(typeof(PersonaId), persona) ? persona.ToString() : "Unknown";
        }
    }
}
