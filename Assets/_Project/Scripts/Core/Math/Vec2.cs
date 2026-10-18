using System;
using System.Globalization;

namespace Arena.Core.Math
{
    /// <summary>
    /// 2D vector of the simulation plane (D-005, D-009): Core (x, y) maps to Unity (x, 0, y), 1 unit = 1 cell.
    /// Blittable; equality is exact float comparison, which is what determinism and snapshot checks need.
    /// </summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => default;
        public static Vec2 Right => new Vec2(1f, 0f);
        public static Vec2 Up => new Vec2(0f, 1f);

        public float SqrLength => X * X + Y * Y;
        public float Length => MathF.Sqrt(X * X + Y * Y);

        /// <summary>Unit vector in the same direction, or zero if the length is below <see cref="MathUtil.Epsilon"/>.</summary>
        public Vec2 Normalized
        {
            get
            {
                float len = Length;
                return len > MathUtil.Epsilon ? new Vec2(X / len, Y / len) : Zero;
            }
        }

        public bool IsZero => SqrLength <= MathUtil.Epsilon * MathUtil.Epsilon;
        public bool IsFinite => MathUtil.IsFinite(X) && MathUtil.IsFinite(Y);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float k) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator *(float k, Vec2 a) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator /(Vec2 a, float k) => new Vec2(a.X / k, a.Y / k);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>Z component of the 3D cross product (positive if b is counter-clockwise from a).</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrLength;

        /// <summary>Linear interpolation, <paramref name="t"/> clamped to [0, 1].</summary>
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
        {
            t = MathUtil.Clamp01(t);
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        /// <summary>Returns <paramref name="v"/> shortened to <paramref name="maxLength"/> if it is longer.</summary>
        public static Vec2 ClampLength(Vec2 v, float maxLength)
        {
            float sqr = v.SqrLength;
            if (sqr <= maxLength * maxLength) return v;
            float k = maxLength / MathF.Sqrt(sqr);
            return new Vec2(v.X * k, v.Y * k);
        }

        public static bool Approximately(Vec2 a, Vec2 b, float tolerance) =>
            MathUtil.Approximately(a.X, b.X, tolerance) && MathUtil.Approximately(a.Y, b.Y, tolerance);

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() =>
            "(" + X.ToString("0.###", CultureInfo.InvariantCulture) + ", " + Y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }
}
