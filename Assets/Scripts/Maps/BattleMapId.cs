using System;
using System.Collections.Generic;

namespace Ring.Maps
{
    /// <summary>
    /// 一局战斗随机到的地图编号，共 6 张，值 0..5 连续。
    ///
    /// 成员名刻意保持中性的 Map01..Map06：它们是 Inspector 里已序列化的枚举值，改名会让
    /// 场景 / 预制体里已存下来的 Fixed Map 等字段产生歧义。六张地图的真实身份（中文名、
    /// Z 区根节点名、MapKit 资源目录、配置键、机制脚本）统一放在 <see cref="BattleMapInfo"/>
    /// 映射表里，由 <see cref="BattleMaps.Info"/> 按编号查询；值不动即可随时扩充。
    ///
    /// 与 B8「六区域」的区别：本枚举是「这一局选哪张地图」；每张地图内部的 6 个区域锚点由
    /// <see cref="MapScene"/> 的 RegionAnchors 提供，属于另一个层次。上传的 MapDesign 场景里
    /// Z1..Z6 是地图设计验证用的六个区域根节点，与本枚举 0..5 一一对应。
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
    /// 一张地图在 MapKit 资源包里的静态身份。纯 C#、零 UnityEngine，可被离线回归直接编入。
    /// 所有字符串都是「事实常量」（目录名 / 配置键 / 脚本类名），MapKit 资产改名时只需改这一张表。
    /// 本类不含运行时状态，也不引用 MapKitTest 命名空间 —— Ring.Maps 与 MapKit 保持单向解耦。
    /// </summary>
    public sealed class BattleMapInfo
    {
        public BattleMapId Id { get; private set; }

        /// <summary>日志与 HUD 用的中文名，带 Z 编号（如「Z1 雨夜竹林」）。</summary>
        public string DisplayName { get; private set; }

        /// <summary>设计场景里的区域根节点名（如「Z1_雨夜竹林」），供按名查找与文档对照。</summary>
        public string ZoneName { get; private set; }

        /// <summary>Assets/MapKit/Maps/ 下的资源目录名（如「01_BambooNight」）。</summary>
        public string ZoneFolder { get; private set; }

        /// <summary>map_config.json 里本图机制的参数键（如「bamboo」）。</summary>
        public string ConfigKey { get; private set; }

        /// <summary>本图机制脚本类名（MapKitTest 命名空间，如「BambooNightMechanic」）。</summary>
        public string MechanicScript { get; private set; }

        /// <summary>机制一句话说明，供 HUD 与文档展示。</summary>
        public string MechanicSummary { get; private set; }

        public BattleMapInfo(BattleMapId id, string displayName, string zoneName, string zoneFolder,
            string configKey, string mechanicScript, string mechanicSummary)
        {
            Id = id;
            DisplayName = displayName;
            ZoneName = zoneName;
            ZoneFolder = zoneFolder;
            ConfigKey = configKey;
            MechanicScript = mechanicScript;
            MechanicSummary = mechanicSummary;
        }
    }

    /// <summary>
    /// 地图编号的纯 C# 工具：数量、下标换算、随机映射、显示名，以及 MapKit 资产映射查询。
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

        /// <summary>
        /// 按下标（== 枚举值）排列的 MapKit 资产映射，与 All 一一对应。
        /// 顺序来自 Assets/MapKit/Maps/01..06 与上传设计场景的 Z1..Z6，改动前先核对两处。
        /// </summary>
        private static readonly BattleMapInfo[] infos = new[]
        {
            new BattleMapInfo(BattleMapId.Map01, "Z1 雨夜竹林", "Z1_雨夜竹林", "01_BambooNight",
                "bamboo", "BambooNightMechanic", "雷雨：随机落雷，低概率造成 40 点伤害并眩晕 1 秒"),
            new BattleMapInfo(BattleMapId.Map02, "Z2 蒸汽井盖", "Z2_蒸汽井盖", "02_Steam",
                "steam", "SteamMechanic", "蒸汽：每 4~5 秒约 20% 井口预警 1 秒后喷发，单次 5 点伤害"),
            new BattleMapInfo(BattleMapId.Map03, "Z3 竞技场", "Z3_竞技场", "03_Arena",
                "arena", "ArenaMechanic", "竞技场：每 15 秒双方各叠加 15% 攻击力，可叠加"),
            new BattleMapInfo(BattleMapId.Map04, "Z4 精灵森林", "Z4_精灵森林", "04_ElfForest",
                "forest", "ForestMechanic", "精灵森林：抓取藤周期性冒芽、成熟、缠住目标后凋亡"),
            new BattleMapInfo(BattleMapId.Map05, "Z5 钟塔", "Z5_钟塔", "05_ClockTower",
                "clock", "ClockMechanic", "钟塔：每 10 秒钟声三选一（回溯 / 加速 ×1.15 / 减速 ×0.85）"),
            new BattleMapInfo(BattleMapId.Map06, "Z6 洞穴", "Z6_洞窟", "06_Cave",
                "cave", "CaveMechanic", "洞穴：每 8 秒落下 3~10 颗岩石，落地前 1 秒阴影预警，单次 10 点伤害"),
        };

        /// <summary>只读的六图资产映射表；下标 == 枚举值。</summary>
        public static IReadOnlyList<BattleMapInfo> Infos { get { return infos; } }

        /// <summary>按编号取资产映射；非法编号返回 null（调用方判空）。</summary>
        public static BattleMapInfo Info(BattleMapId id)
        {
            return id.IsDefined() ? infos[IndexOf(id)] : null;
        }

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

        /// <summary>
        /// B8 / 文档 [005]：从六区域中抽 count 个**互不重复**的场地，作为并行 1v1 的战斗场地。
        /// 开局固定抽 3 个（3 场 1v1）；count 超过 6 时截到 6，小于等于 0 返回空表。
        /// 随机源由调用方注入（Unity 侧传 System.Random，测试侧传固定 seed），便于离线复现。
        /// </summary>
        public static IReadOnlyList<BattleMapId> PickDistinct(int count, Random random)
        {
            if (count <= 0)
            {
                return new BattleMapId[0];
            }

            int take = count < Count ? count : Count;
            if (random == null)
            {
                random = new Random();
            }

            var pool = new List<BattleMapId>(All);
            Shuffle(pool, random);
            return pool.GetRange(0, take);
        }

        /// <summary>Fisher–Yates 洗牌（纯 C#）；抽场地与三场配对共用同一口径。</summary>
        public static void Shuffle<T>(IList<T> list, Random random)
        {
            if (list == null || list.Count < 2)
            {
                return;
            }

            if (random == null)
            {
                random = new Random();
            }

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        /// <summary>非法编号一律折回第一张，供 Inspector 里手填的调试值兜底。</summary>
        public static BattleMapId ClampToValid(BattleMapId id)
        {
            return id.IsDefined() ? id : All[0];
        }

        /// <summary>真实地图中文名；日志与 HUD 都走这个入口。非法编号返回「未知地图」。</summary>
        public static string ToDisplayName(this BattleMapId id)
        {
            BattleMapInfo info = Info(id);
            return info != null ? info.DisplayName : "未知地图";
        }

        /// <summary>设计场景里的区域根节点名；非法编号返回空串。</summary>
        public static string ToZoneName(this BattleMapId id)
        {
            BattleMapInfo info = Info(id);
            return info != null ? info.ZoneName : string.Empty;
        }

        /// <summary>MapKit 资源目录名（01_BambooNight 等）；非法编号返回空串。</summary>
        public static string ToZoneFolder(this BattleMapId id)
        {
            BattleMapInfo info = Info(id);
            return info != null ? info.ZoneFolder : string.Empty;
        }

        /// <summary>该图资源目录的完整 Assets 路径；非法编号返回空串。</summary>
        public static string ToAssetPath(this BattleMapId id)
        {
            BattleMapInfo info = Info(id);
            return info != null ? "Assets/MapKit/Maps/" + info.ZoneFolder : string.Empty;
        }

        /// <summary>map_config.json 中本图机制的参数键；非法编号返回空串。</summary>
        public static string ToConfigKey(this BattleMapId id)
        {
            BattleMapInfo info = Info(id);
            return info != null ? info.ConfigKey : string.Empty;
        }

        /// <summary>本图机制脚本类名（MapKitTest 命名空间）；非法编号返回空串。</summary>
        public static string ToMechanicScript(this BattleMapId id)
        {
            BattleMapInfo info = Info(id);
            return info != null ? info.MechanicScript : string.Empty;
        }

        /// <summary>本图机制的一句话说明；非法编号返回空串。</summary>
        public static string ToMechanicSummary(this BattleMapId id)
        {
            BattleMapInfo info = Info(id);
            return info != null ? info.MechanicSummary : string.Empty;
        }
    }
}
