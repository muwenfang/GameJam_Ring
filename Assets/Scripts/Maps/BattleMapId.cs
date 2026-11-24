using System;
using System.Collections.Generic;

namespace Ring.Maps
{
    /// <summary>
    /// 一局战斗随机到的地图编号，共 6 张。
    ///
    /// 这里只定义「身份」和「槽位下标」，六张地图的实体（场景层级）还没做，由 BattleMapPresenter
    /// 留出六个挂接槽位；等美术/策划定名后，直接改本枚举成员名即可，值 0..5 必须保持连续，
    /// 因为槽位数组是按枚举值当数组下标一一对应的。
    ///
    /// 与 B8「六区域」的区别：本枚举是「这一局选哪张地图」；
    /// 每张地图内部的 6 个区域锚点由 <see cref="MapScene"/> 的 RegionAnchors 提供，两者是两个层次。
    /// </summary>
    public enum BattleMapId
    {
        Map01 = 0,
        Map02 = 1,
        Map03 = 2,
        Map04 = 3,
        Map05 = 4,
        Map06 = 5,
    }

    /// <summary>
    /// 地图编号的纯 C# 工具：数量、下标换算、随机映射、显示名。
    /// 刻意零 UnityEngine —— 可以脱离 Unity 跑离线回归（见 .check/belltest）。
    /// 随机源由调用方注入：Unity 侧传 UnityEngine.Random.Range 的结果，测试侧传固定 seed 的 System.Random。
    /// </summary>
    public static class BattleMaps
    {
        public const int Count = 6;

        public static readonly IReadOnlyList<BattleMapId> All = new[]
        {
            BattleMapId.Map01,
            BattleMapId.Map02,
            BattleMapId.Map03,
            BattleMapId.Map04,
            BattleMapId.Map05,
            BattleMapId.Map06,
        };

        public static bool IsDefined(this BattleMapId id)
        {
            return (int)id >= 0 && (int)id < Count;
        }

        /// <summary>地图编号 → 槽位下标（0..5）。</summary>
        public static int IndexOf(BattleMapId id)
        {
            return (int)id;
        }

        /// <summary>槽位下标 → 地图编号；越界折回合法范围，不抛异常。</summary>
        public static BattleMapId FromIndex(int index)
        {
            return Pick(index);
        }

        /// <summary>把任意整数映射成合法地图（负数、越界都折回）。Unity 侧把 Random.Range(0, Count) 传进来。</summary>
        public static BattleMapId Pick(int roll)
        {
            int index = roll % Count;
            if (index < 0)
            {
                index += Count;
            }

            return All[index];
        }

        /// <summary>用注入的随机源抽一张；null 时退化成默认随机源，绝不返回非法值。</summary>
        public static BattleMapId Pick(Random random)
        {
            if (random == null)
            {
                random = new Random();
            }

            return Pick(random.Next(Count));
        }

        /// <summary>非法编号一律折回第一张，供 Inspector 里手填的调试值兜底。</summary>
        public static BattleMapId ClampToValid(BattleMapId id)
        {
            return IsDefined(id) ? id : All[0];
        }

        /// <summary>占位显示名；六张地图定名后改这里即可，日志与 HUD 都用这个入口。</summary>
        public static string ToDisplayName(this BattleMapId id)
        {
            return IsDefined(id) ? "地图 " + ((int)id + 1) : "未知地图";
        }
    }
}
