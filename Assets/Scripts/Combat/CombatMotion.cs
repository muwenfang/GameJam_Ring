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

        /// <summary>
        /// 矩形场地约束下的移动：轴分离滑墙 + 轴对齐矩形边界。
        ///
        /// 与 <see cref="Move"/> 的圆形约束并存，且刻意不改动 <see cref="Move"/>：
        /// 25×12 的横版地图是矩形，圆形边界会把站在地面上沿的角色卡在圆弧最低点，
        /// 向下（S）被裁掉、向上（W）被夹到离地的位置（看起来像跳跃）。
        /// 保留圆形版本是为了让既有离线回归（.check/belltest）与旧分擂台逻辑不受影响。
        /// </summary>
        /// <param name="rectMin">矩形左下角（世界坐标，未内缩角色半径）。</param>
        /// <param name="rectMax">矩形右上角（世界坐标，未内缩角色半径）。</param>
        public static Vec2 MoveRect(Vec2 position, Vec2 delta, float radius, IReadOnlyList<CombatWall> walls,
            Vec2 rectMin, Vec2 rectMax)
        {
            // 边界按角色半径内缩，避免半个身子出界。
            float radiusSafe = Math.Max(0f, radius);
            float minX = rectMin.X + radiusSafe;
            float minY = rectMin.Y + radiusSafe;
            float maxX = rectMax.X - radiusSafe;
            float maxY = rectMax.Y - radiusSafe;

            // 矩形比 2×半径还窄时退化成中线，避免 min/max 倒置导致 Clamp 结果乱跳。
            if (minX > maxX)
            {
                float midX = (minX + maxX) * 0.5f;
                minX = maxX = midX;
            }

            if (minY > maxY)
            {
                float midY = (minY + maxY) * 0.5f;
                minY = maxY = midY;
            }

            var lower = new Vec2(minX, minY);
            var upper = new Vec2(maxX, maxY);

            if (delta.SqrMagnitude <= CombatMath.Epsilon)
            {
                return ClampToRect(position, lower, upper);
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

                current = ClampToRect(current, lower, upper);
            }

            return current;
        }

        /// <summary>
        /// 地面锁定移动（横版左右）：Y 一律固定在 <paramref name="groundY"/>，只解算 X 位移。
        ///
        /// 用于「角色只能沿地面左右走」的口径（2026-11-28 策划确认）：
        /// 任何来源的竖向位移（W/S 输入、击退、击飞的水平抛物线残差、拉回目标的高度差）都被丢弃，
        /// 跳跃则完全交给 <c>CombatBody</c> 的 <c>JumpOffset</c> 视觉偏移，不参与这里的求解。
        /// 墙体仍然生效：先尝试 X 位移，撞墙就不再前进，再夹进左右边界。
        /// </summary>
        /// <param name="minX">可走区间左端（世界坐标，未内缩角色半径）。</param>
        /// <param name="maxX">可走区间右端（世界坐标，未内缩角色半径）。</param>
        /// <param name="groundY">角色脚底所在的地面线世界 Y。</param>
        public static Vec2 MoveGround(Vec2 position, Vec2 delta, float radius, IReadOnlyList<CombatWall> walls,
            float minX, float maxX, float groundY)
        {
            float inset = Math.Max(0f, radius);
            float lo = minX + inset;
            float hi = maxX - inset;

            // 可走区间比 2×半径还窄时退化成单点，避免 lo > hi 时 Clamp 结果乱跳。
            if (lo > hi)
            {
                float mid = (lo + hi) * 0.5f;
                lo = hi = mid;
            }

            Vec2 current = new Vec2(position.X, groundY);
            if (Math.Abs(delta.X) <= CombatMath.Epsilon)
            {
                return new Vec2(CombatMath.Clamp(current.X, lo, hi), groundY);
            }

            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta.X) / MaxStep));
            steps = Math.Min(steps, 64);
            float stepX = delta.X / steps;
            for (int i = 0; i < steps; i++)
            {
                Vec2 next = new Vec2(current.X + stepX, groundY);
                if (!Blocked(current, next, radius, walls))
                {
                    current = next;
                }

                current = new Vec2(CombatMath.Clamp(current.X, lo, hi), groundY);
            }

            return current;
        }

        /// <summary>把点夹进轴对齐矩形；min/max 已由调用方内缩过角色半径。</summary>
        public static Vec2 ClampToRect(Vec2 position, Vec2 min, Vec2 max)
        {
            return new Vec2(
                CombatMath.Clamp(position.X, min.X, max.X),
                CombatMath.Clamp(position.Y, min.Y, max.Y));
        }
    }
}
