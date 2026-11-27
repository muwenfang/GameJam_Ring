using System;
using Ring.Abilities;

namespace Ring.Combat
{
    /// <summary>M2.5 一次完整战斗快照：生命、坐标、护盾、增益/减益、姿态、能力限时修正。</summary>
    public struct CharacterSnapshot
    {
        public float Time;
        public float Health;
        public float X;
        public float Y;
        public float Shield;
        public float ShieldRemaining;
        public StatusSnapshot[] Statuses;
        public StanceSnapshot Stance;
        public AbilityModifierState[] Modifiers;
        public bool Valid;

        public override string ToString()
        {
            return "Snapshot(t=" + Time.ToString("0.##") + ", hp=" + Health.ToString("0.##")
                   + ", pos=(" + X.ToString("0.##") + "," + Y.ToString("0.##") + "), buffs="
                   + (Statuses != null ? Statuses.Length : 0) + ")";
        }
    }

    /// <summary>0.1s 采样、约 3.2 秒容量的环形缓冲。所有回滚来源共用。</summary>
    public sealed class SnapshotRing
    {
        public const int DefaultCapacity = 32;
        public const float DefaultInterval = 0.1f;

        private readonly CharacterSnapshot[] buffer;
        private readonly float interval;
        private int count;
        private int head;

        public SnapshotRing(int capacity = DefaultCapacity, float interval = DefaultInterval)
        {
            buffer = new CharacterSnapshot[Math.Max(2, capacity)];
            this.interval = Math.Max(0.01f, interval);
        }

        public float Interval => interval;
        public int Count => count;
        public int Capacity => buffer.Length;

        public void Clear()
        {
            count = 0;
            head = 0;
        }

        public void Push(CharacterSnapshot snapshot)
        {
            snapshot.Valid = true;
            buffer[head] = snapshot;
            head = (head + 1) % buffer.Length;
            if (count < buffer.Length)
            {
                count++;
            }
        }

        /// <summary>找时间上最接近 now-secondsAgo 的样本。</summary>
        public bool TryGetNearest(float now, float secondsAgo, out CharacterSnapshot snapshot)
        {
            float target = now - Math.Max(0f, secondsAgo);
            float bestDistance = float.MaxValue;
            snapshot = default;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                CharacterSnapshot candidate = buffer[i];
                if (!candidate.Valid)
                {
                    continue;
                }

                float distance = Math.Abs(candidate.Time - target);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                snapshot = candidate;
                found = true;
            }

            return found;
        }

        public bool TryGetOldest(out CharacterSnapshot snapshot)
        {
            float bestTime = float.MaxValue;
            snapshot = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (!buffer[i].Valid || buffer[i].Time >= bestTime)
                {
                    continue;
                }

                bestTime = buffer[i].Time;
                snapshot = buffer[i];
                found = true;
            }

            return found;
        }
    }

    public readonly struct RewindPlan
    {
        public readonly CharacterSnapshot Snapshot;
        public readonly StateRestoreMode Mode;
        public readonly bool RestorePosition;
        public readonly float HealthToApply;

        public RewindPlan(CharacterSnapshot snapshot, StateRestoreMode mode, bool restorePosition, float healthToApply)
        {
            Snapshot = snapshot;
            Mode = mode;
            RestorePosition = restorePosition;
            HealthToApply = healthToApply;
        }
    }

    public static class RewindPlanner
    {
        /// <summary>
        /// 生成回滚方案。PhysiologicalOnly 只还原生命/护盾/增益，不动坐标；Full 连坐标一起还原。
        /// noBackslide=true 时生命不低于当前值；maxHeal&gt;0 时本次最多回复这么多。
        /// </summary>
        public static bool TryBuild(SnapshotRing ring, float now, float secondsAgo, StateRestoreMode mode,
            float currentHealth, float maxHealth, float maxHeal, bool noBackslide, out RewindPlan plan)
        {
            plan = default;
            CharacterSnapshot snapshot;
            if (ring == null || !ring.TryGetNearest(now, secondsAgo, out snapshot))
            {
                return false;
            }

            float health = Math.Max(1f, snapshot.Health);
            if (noBackslide)
            {
                health = Math.Max(health, currentHealth);
            }

            if (maxHeal > 0f)
            {
                health = Math.Min(health, currentHealth + maxHeal);
            }

            health = CombatMath.Clamp(health, 1f, Math.Max(1f, maxHealth));
            plan = new RewindPlan(snapshot, mode, mode == StateRestoreMode.Full, health);
            return true;
        }
    }
}
