using System;
using System.Collections.Generic;

namespace Ring.Combat
{
    /// <summary>轴对齐墙体（格坐标）。地面/场景边界由外部注册。</summary>
    public readonly struct CombatWall
    {
        public readonly Vec2 Center;
        public readonly Vec2 Size;

        public CombatWall(Vec2 center, Vec2 size)
        {
            Center = center;
            Size = new Vec2(Math.Max(0f, size.X), Math.Max(0f, size.Y));
        }

        public CombatWall(float centerX, float centerY, float width, float height)
            : this(new Vec2(centerX, centerY), new Vec2(width, height))
        {
        }

        public bool ContainsCircle(Vec2 point, float radius)
        {
            return CombatMath.CircleIntersectsRect(point, Math.Max(0f, radius), Center, Size);
        }

        public override string ToString() => "Wall(" + Center + ", " + Size + ")";
    }

    /// <summary>
    /// 移动求解：轴分离滑墙 + 圆形场地约束。击退/冲刺/强制拉回都走这里，保证不穿墙。
    /// </summary>
    public static class MotionSolver
    {
        private const float MaxStep = 0.2f;

        public static Vec2 Move(Vec2 position, Vec2 delta, float radius, IReadOnlyList<CombatWall> walls,
            bool clampToArena, Vec2 arenaCenter, float arenaRadius)
        {
            if (delta.SqrMagnitude <= CombatMath.Epsilon)
            {
                return clampToArena ? ClampToCircle(position, arenaCenter, arenaRadius) : position;
            }

            int steps = Math.Max(1, (int)Math.Ceiling(delta.Magnitude / MaxStep));
            steps = Math.Min(steps, 64);
            Vec2 step = delta / steps;
            Vec2 current = position;
            for (int i = 0; i < steps; i++)
            {
                Vec2 tryX = new Vec2(current.X + step.X, current.Y);
                if (!Blocked(current, tryX, radius, walls))
                {
                    current = tryX;
                }

                Vec2 tryY = new Vec2(current.X, current.Y + step.Y);
                if (!Blocked(current, tryY, radius, walls))
                {
                    current = tryY;
                }

                if (clampToArena)
                {
                    current = ClampToCircle(current, arenaCenter, arenaRadius);
                }
            }

            return current;
        }

        public static bool Blocked(Vec2 from, Vec2 to, float radius, IReadOnlyList<CombatWall> walls)
        {
            if (walls == null)
            {
                return false;
            }

            for (int i = 0; i < walls.Count; i++)
            {
                CombatWall wall = walls[i];
                // 已经卡在墙里时允许继续移动，避免被墙永久吸住。
                if (wall.ContainsCircle(to, radius) && !wall.ContainsCircle(from, radius))
                {
                    return true;
                }
            }

            return false;
        }

        public static Vec2 ClampToCircle(Vec2 position, Vec2 center, float radius)
        {
            if (radius <= 0f)
            {
                return position;
            }

            Vec2 offset = position - center;
            float distance = offset.Magnitude;
            if (distance <= radius || distance <= CombatMath.Epsilon)
            {
                return position;
            }

            return center + offset * (radius / distance);
        }
    }
}
