using System;

namespace Arena.Core.Math
{
    /// <summary>
    /// Scalar helpers on float (System.MathF). Angles are in degrees, 0° = +X, counter-clockwise.
    /// Note: inside <c>Arena.Core.*</c> the name <c>Math</c> resolves to this namespace — use <c>MathF</c> / <c>MathUtil</c>.
    /// </summary>
    public static class MathUtil
    {
        /// <summary>Technical tolerance for "zero length" checks (not a gameplay parameter).</summary>
        public const float Epsilon = 1e-6f;

        /// <summary>Technical tolerance for distances accumulated over many ticks (projectile range), not a gameplay parameter.</summary>
        public const float DistanceEpsilon = 1e-4f;

        public const float DegToRad = MathF.PI / 180f;
        public const float RadToDeg = 180f / MathF.PI;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        /// <summary>Linear interpolation, <paramref name="t"/> clamped to [0, 1].</summary>
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float Abs(float v) => v < 0f ? -v : v;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;

        public static bool Approximately(float a, float b, float tolerance) => Abs(a - b) <= tolerance;

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Rounds half away from zero (MathF.Round uses banker's rounding).</summary>
        public static int RoundToInt(float v) => v >= 0f ? (int)MathF.Floor(v + 0.5f) : -(int)MathF.Floor(-v + 0.5f);

        /// <summary>Unit vector for an angle in degrees (0° = +X, 90° = +Y).</summary>
        public static Vec2 AngleToDir(float degrees)
        {
            float rad = degrees * DegToRad;
            return new Vec2(MathF.Cos(rad), MathF.Sin(rad));
        }

        /// <summary>Angle of a direction in degrees in (-180, 180]; a zero vector gives 0.</summary>
        public static float DirToAngle(Vec2 dir) => dir.IsZero ? 0f : MathF.Atan2(dir.Y, dir.X) * RadToDeg;

        /// <summary>Rotates a vector counter-clockwise by <paramref name="degrees"/>.</summary>
        public static Vec2 Rotate(Vec2 v, float degrees)
        {
            float rad = degrees * DegToRad;
            float c = MathF.Cos(rad);
            float s = MathF.Sin(rad);
            return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        /// <summary>Unsigned angle between two directions in degrees [0, 180]; 0 if either is zero.</summary>
        public static float AngleBetween(Vec2 a, Vec2 b)
        {
            if (a.IsZero || b.IsZero) return 0f;
            float cos = Clamp(Vec2.Dot(a.Normalized, b.Normalized), -1f, 1f);
            return MathF.Acos(cos) * RadToDeg;
        }
    }
}
