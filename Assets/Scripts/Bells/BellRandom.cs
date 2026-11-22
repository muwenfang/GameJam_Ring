using System;
using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>
    /// 钟声系统使用的随机源。抽成接口是为了让「打乱」「随机选种类」在离线测试里可以复现，
    /// 也让运行时可以换成带种子的实现（录像回放、平衡性测试）。
    /// </summary>
    public interface IBellRandom
    {
        /// <summary>返回 [minInclusive, maxExclusive) 的整数。</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>原地 Fisher–Yates 打乱。</summary>
        void Shuffle<T>(IList<T> list);
    }

    /// <summary>
    /// 确定性随机源（xorshift32）。不依赖 UnityEngine.Random，因此纯 C# 测试可用。
    /// 相同种子 + 相同调用序列 → 完全相同的结果。
    /// </summary>
    public sealed class BellRandom : IBellRandom
    {
        private uint state;

        public BellRandom(int seed)
        {
            // 0 是 xorshift 的不动点，必须换掉。
            state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        public static BellRandom CreateWithTimeSeed()
        {
            return new BellRandom(Environment.TickCount);
        }

        public uint State
        {
            get { return state; }
        }

        private uint NextUInt()
        {
            // xorshift32，参数 (13, 17, 5) 是 Marsaglia 给出的经典取值。
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        public void Shuffle<T>(IList<T> list)
        {
            if (list == null || list.Count < 2)
            {
                return;
            }

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
