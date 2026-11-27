using System;

namespace Ring.Combat
{
    /// <summary>纯 C# 二维向量，战斗核心不依赖 UnityEngine，方便离线断言。</summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0f, 0f);
        public static Vec2 Right => new Vec2(1f, 0f);
        public static Vec2 Up => new Vec2(0f, 1f);

        public float SqrMagnitude => X * X + Y * Y;
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        public Vec2 Normalized
        {
            get
            {
                float magnitude = Magnitude;
                return magnitude <= CombatMath.Epsilon ? Zero : new Vec2(X / magnitude, Y / magnitude);
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float scalar) => new Vec2(a.X * scalar, a.Y * scalar);
        public static Vec2 operator *(float scalar, Vec2 a) => new Vec2(a.X * scalar, a.Y * scalar);
        public static Vec2 operator /(Vec2 a, float scalar) => Math.Abs(scalar) <= CombatMath.Epsilon ? Zero : new Vec2(a.X / scalar, a.Y / scalar);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
        {
            t = CombatMath.Clamp01(t);
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        public bool Equals(Vec2 other) => Math.Abs(X - other.X) <= CombatMath.Epsilon && Math.Abs(Y - other.Y) <= CombatMath.Epsilon;
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ")";

        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);
    }

    public static class CombatMath
    {
        public const float Epsilon = 0.0001f;
        public const float DegreesToRadians = (float)Math.PI / 180f;
        public const float RadiansToDegrees = 180f / (float)Math.PI;

        public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) <= Epsilon;

        public static Vec2 SafeNormalize(Vec2 value, Vec2 fallback)
        {
            float magnitude = value.Magnitude;
            return magnitude <= Epsilon ? fallback : new Vec2(value.X / magnitude, value.Y / magnitude);
        }

        public static float AngleDegrees(Vec2 direction) => (float)Math.Atan2(direction.Y, direction.X) * RadiansToDegrees;

        public static Vec2 FromAngleDegrees(float degrees)
        {
            float radians = degrees * DegreesToRadians;
            return new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians));
        }

        public static Vec2 Rotate(Vec2 value, float degrees)
        {
            float radians = degrees * DegreesToRadians;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new Vec2(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
        }

        public static float SignedAngleDegrees(Vec2 from, Vec2 to)
        {
            float angle = AngleDegrees(to) - AngleDegrees(from);
            while (angle > 180f)
            {
                angle -= 360f;
            }

            while (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }

        /// <summary>点到线段的最短距离；line 退化成一个点时退化为点距。</summary>
        public static float DistancePointToSegment(Vec2 point, Vec2 start, Vec2 end)
        {
            Vec2 segment = end - start;
            float lengthSquared = segment.SqrMagnitude;
            if (lengthSquared <= Epsilon)
            {
                return Distance(point, start);
            }

            float t = Clamp01(Vec2.Dot(point - start, segment) / lengthSquared);
            Vec2 closest = start + segment * t;
            return Distance(point, closest);
        }

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;

        /// <summary>圆与轴对齐矩形是否相交。</summary>
        public static bool CircleIntersectsRect(Vec2 center, float radius, Vec2 rectCenter, Vec2 rectSize)
        {
            float halfX = Math.Max(0f, rectSize.X) * 0.5f;
            float halfY = Math.Max(0f, rectSize.Y) * 0.5f;
            float closestX = Clamp(center.X, rectCenter.X - halfX, rectCenter.X + halfX);
            float closestY = Clamp(center.Y, rectCenter.Y - halfY, rectCenter.Y + halfY);
            float dx = center.X - closestX;
            float dy = center.Y - closestY;
            return dx * dx + dy * dy < Math.Max(0f, radius) * Math.Max(0f, radius);
        }
    }
}
