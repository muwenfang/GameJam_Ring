using System;
using System.Collections.Generic;
using Ring.Abilities;

namespace Ring.Combat
{
    public enum HitShapeKind
    {
        Rect = 0,
        Circle = 1,
        Fan = 2,
        Line = 3
    }

    /// <summary>命中查询用的目标快照，避免核心直接依赖 MonoBehaviour。</summary>
    public struct CombatTargetInfo
    {
        public int Id;
        public int OpponentId;
        public Vec2 Position;
        public float Radius;
        public bool IsAlive;
        public bool IsInPlay;

        public CombatTargetInfo(int id, int opponentId, Vec2 position, float radius, bool isAlive, bool isInPlay)
        {
            Id = id;
            OpponentId = opponentId;
            Position = position;
            Radius = radius;
            IsAlive = isAlive;
            IsInPlay = isInPlay;
        }
    }

    /// <summary>
    /// 统一格坐标命中形状：矩形、圆、扇形、直线。所有技能/平A/弹道共用一套口径。
    /// </summary>
    public sealed class HitShape
    {
        public HitShapeKind Kind;
        public float Length;
        public float Width;
        public float Radius;
        public float AngleDegrees;

        public static HitShape Rect(float length, float width)
        {
            return new HitShape { Kind = HitShapeKind.Rect, Length = Math.Max(0f, length), Width = Math.Max(0f, width) };
        }

        public static HitShape Line(float length, float width)
        {
            return new HitShape { Kind = HitShapeKind.Line, Length = Math.Max(0f, length), Width = Math.Max(0f, width) };
        }

        public static HitShape Circle(float radius)
        {
            return new HitShape { Kind = HitShapeKind.Circle, Radius = Math.Max(0f, radius) };
        }

        public static HitShape Fan(float range, float angleDegrees)
        {
            return new HitShape { Kind = HitShapeKind.Fan, Length = Math.Max(0f, range), AngleDegrees = Math.Max(1f, angleDegrees) };
        }

        public bool Contains(Vec2 origin, Vec2 facing, Vec2 point)
        {
            Vec2 direction = CombatMath.SafeNormalize(facing, Vec2.Right);
            Vec2 offset = point - origin;
            switch (Kind)
            {
                case HitShapeKind.Circle:
                    return offset.Magnitude <= Radius;

                case HitShapeKind.Fan:
                    {
                        float distance = offset.Magnitude;
                        if (distance > Length)
                        {
                            return false;
                        }

                        if (distance <= CombatMath.Epsilon)
                        {
                            return true;
                        }

                        float angle = Math.Abs(CombatMath.SignedAngleDegrees(direction, offset));
                        return angle <= AngleDegrees * 0.5f;
                    }

                default:
                    {
                        float forward = Vec2.Dot(offset, direction);
                        float lateral = Math.Abs(Vec2.Cross(direction, offset));
                        return forward >= -CombatMath.Epsilon && forward <= Length && lateral <= Width * 0.5f + CombatMath.Epsilon;
                    }
            }
        }

        public bool IntersectsCircle(Vec2 origin, Vec2 facing, Vec2 center, float targetRadius)
        {
            targetRadius = Math.Max(0f, targetRadius);
            if (Contains(origin, facing, center))
            {
                return true;
            }

            if (targetRadius <= CombatMath.Epsilon)
            {
                return false;
            }

            Vec2 direction = CombatMath.SafeNormalize(facing, Vec2.Right);
            Vec2 offset = center - origin;
            switch (Kind)
            {
                case HitShapeKind.Circle:
                    return offset.Magnitude <= Radius + targetRadius;

                case HitShapeKind.Fan:
                    {
                        float distance = offset.Magnitude;
                        if (distance > Length + targetRadius)
                        {
                            return false;
                        }

                        if (distance <= targetRadius)
                        {
                            return true;
                        }

                        float angle = Math.Abs(CombatMath.SignedAngleDegrees(direction, offset));
                        float angularRadius = (float)Math.Asin(CombatMath.Clamp01(targetRadius / distance)) * CombatMath.RadiansToDegrees;
                        return angle <= AngleDegrees * 0.5f + angularRadius;
                    }

                default:
                    {
                        float forward = Vec2.Dot(offset, direction);
                        float lateral = Vec2.Cross(direction, offset);
                        float clampedForward = CombatMath.Clamp(forward, 0f, Length);
                        float halfWidth = Width * 0.5f;
                        float clampedLateral = CombatMath.Clamp(lateral, -halfWidth, halfWidth);
                        Vec2 nearest = origin + direction * clampedForward + new Vec2(-direction.Y, direction.X) * clampedLateral;
                        return CombatMath.Distance(nearest, center) <= targetRadius;
                    }
            }
        }

        public void Query(Vec2 origin, Vec2 facing, IReadOnlyList<CombatTargetInfo> targets, List<int> results, int excludeId = -1)
        {
            if (targets == null || results == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                CombatTargetInfo target = targets[i];
                if (target.Id == excludeId || !target.IsAlive || !target.IsInPlay)
                {
                    continue;
                }

                if (IntersectsCircle(origin, facing, target.Position, target.Radius))
                {
                    results.Add(target.Id);
                }
            }
        }

        /// <summary>直线/矩形取射程内最近的一个目标；命中首个即停时用。</summary>
        public int QueryFirst(Vec2 origin, Vec2 facing, IReadOnlyList<CombatTargetInfo> targets, int excludeId = -1)
        {
            if (targets == null)
            {
                return -1;
            }

            Vec2 direction = CombatMath.SafeNormalize(facing, Vec2.Right);
            int bestId = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < targets.Count; i++)
            {
                CombatTargetInfo target = targets[i];
                if (target.Id == excludeId || !target.IsAlive || !target.IsInPlay)
                {
                    continue;
                }

                if (!IntersectsCircle(origin, direction, target.Position, target.Radius))
                {
                    continue;
                }

                float distance = Vec2.Dot(target.Position - origin, direction);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestId = target.Id;
                }
            }

            return bestId;
        }

        /// <summary>从技能定义推导形状；单位为格，调用方负责乘 unitsPerCell。</summary>
        public static HitShape FromDefinition(AbilityDefinition definition)
        {
            if (definition == null)
            {
                return Circle(0.5f);
            }

            switch (definition.Shape)
            {
                case AbilityShape.Rect:
                    return Rect(
                        Math.Max(0.5f, definition.AreaLengthCells > 0f ? definition.AreaLengthCells : definition.RangeCells),
                        Math.Max(0.5f, definition.AreaWidthCells));

                case AbilityShape.Line:
                    return Line(
                        Math.Max(0.5f, Math.Max(definition.RangeCells, definition.AreaLengthCells)),
                        Math.Max(0.5f, definition.AreaWidthCells));

                case AbilityShape.Fan:
                    return Fan(
                        Math.Max(0.5f, definition.RangeCells),
                        Math.Max(1f, definition.SpreadDegrees > 0f ? definition.SpreadDegrees : 30f));

                case AbilityShape.Circle:
                    return Circle(Math.Max(0.5f, definition.RadiusCells));

                case AbilityShape.Ground:
                    return Circle(Math.Max(0.5f, definition.RadiusCells > 0f ? definition.RadiusCells : 1f));

                default:
                    return Circle(0.5f);
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case HitShapeKind.Circle:
                    return "Circle(r=" + Radius.ToString("0.##") + ")";
                case HitShapeKind.Fan:
                    return "Fan(range=" + Length.ToString("0.##") + ", " + AngleDegrees.ToString("0.#") + "°)";
                case HitShapeKind.Rect:
                    return "Rect(" + Length.ToString("0.##") + "×" + Width.ToString("0.##") + ")";
                default:
                    return "Line(" + Length.ToString("0.##") + "×" + Width.ToString("0.##") + ")";
            }
        }
    }
}
