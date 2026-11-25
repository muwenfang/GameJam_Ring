using System;
using System.Collections.Generic;
using System.Text;

namespace Ring.Maps
{
    /// <summary>
    /// 一场 1v1 的「场地 + 双方」。三场并行 1v1 中的一场。
    ///
    /// 纯 C#，零 UnityEngine：本文件与 <see cref="BattleMapId"/> 一样可以编进离线回归工程，
    /// 所以「六区域抽三场 / 两两配对 / 位置钟声后重匹配」这些口径都有断言保护。
    ///
    /// 区域口径：六个区域就是 <see cref="BattleMapId.Map01"/>..<see cref="BattleMapId.Map06"/>
    /// （= MapKit 的 Z1..Z6）。每轮从中抽若干个互不重复的场地，每场 1v1 独占一个场地。
    /// </summary>
    public sealed class ArenaDuel
    {
        public ArenaDuel(BattleMapId map, int firstId, int secondId, bool againstClone)
        {
            Map = map;
            FirstId = firstId;
            SecondId = secondId;
            AgainstClone = againstClone;
        }

        /// <summary>本场使用的区域（六区域之一）。</summary>
        public BattleMapId Map { get; }

        public int FirstId { get; }

        /// <summary>对手 id；-1 表示「落单」——复制体对决（B12）或本轮轮空。</summary>
        public int SecondId { get; }

        /// <summary>B12：淘汰制人数为奇数时，落单者与自己的复制体对决。</summary>
        public bool AgainstClone { get; }

        public bool Contains(int participantId)
        {
            return FirstId == participantId || SecondId == participantId;
        }

        /// <summary>对手 id；不在这场里、或落单时返回 -1。</summary>
        public int OpponentOf(int participantId)
        {
            if (participantId == FirstId)
            {
                return SecondId;
            }

            return participantId == SecondId ? FirstId : -1;
        }

        public override string ToString()
        {
            string second = SecondId >= 0
                ? SecondId.ToString()
                : (AgainstClone ? "复制体" : "轮空");
            return Map.ToDisplayName() + "：" + FirstId + " vs " + second;
        }
    }

    /// <summary>
    /// 一轮完整的场地与对手安排：抽中的区域 + 若干场 1v1。
    ///
    /// 这是「角色 ↔ 区域 ↔ 对手」映射的只读快照。它与钟声系统的卡牌映射是两件事：
    /// 钟声系统（BellRuntime）独占改写特质/技能，本类只由流程层（GameFlow）在
    /// 「开局 / 位置钟声 / B8 死亡重分配」这三个时机重建，不存在第二处改写者。
    /// </summary>
    public sealed class ArenaPlan
    {
        public ArenaPlan(IReadOnlyList<BattleMapId> activeMaps, IReadOnlyList<ArenaDuel> duels)
        {
            ActiveMaps = activeMaps ?? new BattleMapId[0];
            Duels = duels ?? new ArenaDuel[0];
        }

        /// <summary>本轮抽中并实际启用的区域（开局固定 3 个，淘汰制随人数减少）。</summary>
        public IReadOnlyList<BattleMapId> ActiveMaps { get; }

        public IReadOnlyList<ArenaDuel> Duels { get; }

        /// <summary>该角色所在的区域（B8「重新分配到六区域之一」要读的就是它）。</summary>
        public BattleMapId MapOf(int participantId)
        {
            ArenaDuel duel = DuelOf(participantId);
            return duel != null ? duel.Map : BattleMapId.Map01;
        }

        /// <summary>该角色当前的 1v1 对手；-1 表示没有（复制体 / 轮空 / 不在场）。</summary>
        public int OpponentOf(int participantId)
        {
            ArenaDuel duel = DuelOf(participantId);
            return duel != null ? duel.OpponentOf(participantId) : -1;
        }

        public ArenaDuel DuelOf(int participantId)
        {
            for (int i = 0; i < Duels.Count; i++)
            {
                if (Duels[i] != null && Duels[i].Contains(participantId))
                {
                    return Duels[i];
                }
            }

            return null;
        }

        public int DuelIndexOf(int participantId)
        {
            for (int i = 0; i < Duels.Count; i++)
            {
                if (Duels[i] != null && Duels[i].Contains(participantId))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool Contains(int participantId)
        {
            return DuelIndexOf(participantId) >= 0;
        }

        /// <summary>是否所有 id 都恰好被安排进一场（校验用）。</summary>
        public bool CoversExactly(IReadOnlyList<int> ids)
        {
            if (ids == null)
            {
                return Duels.Count == 0;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (!Contains(ids[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public string Describe()
        {
            if (Duels.Count == 0)
            {
                return "（本轮无对战）";
            }

            var builder = new StringBuilder();
            builder.Append("区域：");
            for (int i = 0; i < ActiveMaps.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('、');
                }

                builder.Append(ActiveMaps[i].ToDisplayName());
            }

            for (int i = 0; i < Duels.Count; i++)
            {
                builder.Append(" | ").Append(Duels[i]);
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }
    }

    /// <summary>
    /// 三场 1v1 的纯计算生成器。只产出 <see cref="ArenaPlan"/> 数据，不移动任何 GameObject。
    /// 随机源由调用方注入（Unity 侧传 System.Random，离线测试传固定 seed），保证可复现。
    /// </summary>
    public static class ArenaPlanGenerator
    {
        /// <summary>文档口径：普通阶段固定 3 场 1v1（6 人）。</summary>
        public const int DefaultBattleCount = 3;

        /// <summary>
        /// 生成一轮安排。
        /// </summary>
        /// <param name="orderedIds">
        /// 已排好顺序的在场 id：0-1 一对、2-3 一对……。
        /// 开局由 <see cref="ShuffleIds"/> 洗牌得到；位置钟声后由调用方把 BellResult.Swaps 展开得到。
        /// </param>
        /// <param name="fixedMaps">
        /// 为 null（或数量不足 / 有重复）时，从六区域重新抽不重复的场地；
        /// 否则沿用传入的场地（顺序对应每场）。位置钟声只换对手时传它，B8 重分配时传 null。
        /// </param>
        /// <param name="maxBattles">场次数上限，普通阶段为 3。</param>
        /// <param name="random">注入的随机源。</param>
        /// <param name="allowClone">淘汰制下人数为奇数时，落单者与复制体对决（B12）。</param>
        public static ArenaPlan Create(
            IReadOnlyList<int> orderedIds,
            IReadOnlyList<BattleMapId> fixedMaps,
            int maxBattles,
            Random random,
            bool allowClone)
        {
            if (random == null)
            {
                random = new Random();
            }

            var ids = new List<int>();
            if (orderedIds != null)
            {
                ids.AddRange(orderedIds);
            }

            // 项目固定 6 人 3 场；淘汰制人数减少时场次数随之下降，但不会超过上限。
            int duelCount = (ids.Count + 1) / 2;
            int battles = Math.Min(Math.Max(0, maxBattles), duelCount);

            List<BattleMapId> maps = ResolveMaps(fixedMaps, battles, random);
            var duels = new List<ArenaDuel>(battles);
            for (int i = 0; i < battles; i++)
            {
                int first = ids[i * 2];
                int second = i * 2 + 1 < ids.Count ? ids[i * 2 + 1] : -1;
                duels.Add(new ArenaDuel(maps[i], first, second, second < 0 && allowClone));
            }

            return new ArenaPlan(maps, duels);
        }

        /// <summary>把在场 id 洗牌成两两配对后的顺序。</summary>
        public static List<int> ShuffleIds(IReadOnlyList<int> ids, Random random)
        {
            var copy = new List<int>();
            if (ids != null)
            {
                copy.AddRange(ids);
            }

            Shuffle(copy, random);
            return copy;
        }

        /// <summary>Fisher–Yates 洗牌；random 为 null 时退化成默认随机源。</summary>
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

        private static List<BattleMapId> ResolveMaps(
            IReadOnlyList<BattleMapId> fixedMaps, int battles, Random random)
        {
            if (battles <= 0)
            {
                return new List<BattleMapId>();
            }

            if (fixedMaps != null && fixedMaps.Count >= battles)
            {
                var seen = new HashSet<BattleMapId>();
                bool valid = true;
                for (int i = 0; i < battles; i++)
                {
                    if (!BattleMaps.IsDefined(fixedMaps[i]) || !seen.Add(fixedMaps[i]))
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    var reuse = new List<BattleMapId>(battles);
                    for (int i = 0; i < battles; i++)
                    {
                        reuse.Add(fixedMaps[i]);
                    }

                    return reuse;
                }
            }

            return new List<BattleMapId>(BattleMaps.PickDistinct(battles, random));
        }
    }
}
