using Arena.Core.Math;
using Arena.Net.Protocol;

namespace Arena.Net.Serialization
{
    /// <summary>
    /// Quantization of wire values (Server-Spec §4.3). Decoding is a single division by a constant — no trigonometry — so
    /// a client (IL2CPP ARM64) and the server (CoreCLR x64) restore the same bits from the same integers (§2.4). Steps and
    /// widths are <see cref="NetProtocol"/> constants. Non-finite inputs encode as zero.
    /// </summary>
    public static class Quantize
    {
        private const int PositionMaxValue = (1 << NetProtocol.PositionBits) - 1;
        private const int RadiusMaxValue = (1 << NetProtocol.RadiusBits) - 1;
        private const float FullTurnDegrees = 360f;

        // ---- Scalars in [-1, 1] ----

        /// <summary>Value in [-1, 1] → integer in [-scale, scale] (rounded half away from zero).</summary>
        public static int ToUnit(float value, int scale)
        {
            if (!MathUtil.IsFinite(value)) return 0;
            return MathUtil.RoundToInt(MathUtil.Clamp(value, -1f, 1f) * scale);
        }

        public static float FromUnit(int quantized, int scale) => quantized / (float)scale;

        // ---- Vectors of length ≤ 1 (stick input) ----

        /// <summary>
        /// Quantizes a vector of length ≤ 1. The result always decodes to length² ≤ 1 − 1/<see cref="NetProtocol.UnitLengthMarginDivisor"/>
        /// (integer check, exact), so <c>PlayerInput.Sanitized</c> never re-clamps it on either side. The one exception is an
        /// axis-aligned full deflection (±scale, 0), which decodes to exactly length 1.
        /// </summary>
        public static void ToUnitVector(Vec2 value, int scale, out int x, out int y)
        {
            if (!value.IsFinite)
            {
                x = 0;
                y = 0;
                return;
            }
            value = Vec2.ClampLength(value, 1f);
            x = ToUnit(value.X, scale);
            y = ToUnit(value.Y, scale);
            // An axis-aligned full deflection (±scale, 0) decodes to exactly (±1, 0): float math is exact there, keep it.
            if ((x == 0 && (y == scale || y == -scale)) || (y == 0 && (x == scale || x == -scale))) return;
            long limit = UnitLengthLimit(scale);
            while ((long)x * x + (long)y * y > limit)
            {
                if ((x < 0 ? -x : x) >= (y < 0 ? -y : y)) x += x > 0 ? -1 : 1;
                else y += y > 0 ? -1 : 1;
            }
        }

        public static Vec2 FromUnitVector(int x, int y, int scale) => new Vec2(x / (float)scale, y / (float)scale);

        /// <summary>Largest allowed x² + y² for a quantized unit vector of the given scale.</summary>
        public static long UnitLengthLimit(int scale)
        {
            long sqr = (long)scale * scale;
            long margin = (sqr + NetProtocol.UnitLengthMarginDivisor - 1) / NetProtocol.UnitLengthMarginDivisor;
            return sqr - margin;
        }

        /// <summary>Aim: 2 × int16, /<see cref="NetProtocol.AimScale"/>; zero means "no manual aim".</summary>
        public static void ToAim(Vec2 aim, out short x, out short y)
        {
            ToUnitVector(aim, NetProtocol.AimScale, out int qx, out int qy);
            x = (short)qx;
            y = (short)qy;
        }

        public static Vec2 FromAim(short x, short y) => FromUnitVector(x, y, NetProtocol.AimScale);

        /// <summary>Move: 2 × int8, /<see cref="NetProtocol.MoveScale"/>.</summary>
        public static void ToMove(Vec2 move, out sbyte x, out sbyte y)
        {
            ToUnitVector(move, NetProtocol.MoveScale, out int qx, out int qy);
            x = (sbyte)qx;
            y = (sbyte)qy;
        }

        public static Vec2 FromMove(sbyte x, sbyte y) => FromUnitVector(x, y, NetProtocol.MoveScale);

        // ---- Positions on the map ----

        /// <summary>Map coordinate in cells → u16 in 1/<see cref="NetProtocol.PositionStepsPerCell"/> cell, clamped to the range.</summary>
        public static ushort ToPosition(float cells)
        {
            if (!MathUtil.IsFinite(cells)) return 0;
            int q = MathUtil.RoundToInt(MathUtil.Clamp(cells, 0f, NetProtocol.PositionRangeCells) * NetProtocol.PositionStepsPerCell);
            return (ushort)MathUtil.Clamp(q, 0, PositionMaxValue);
        }

        public static float FromPosition(ushort quantized) => quantized / (float)NetProtocol.PositionStepsPerCell;

        public static float PositionStep => 1f / NetProtocol.PositionStepsPerCell;

        // ---- Angles (views only) ----

        /// <summary>Angle in degrees (any range) → <paramref name="bits"/>-bit integer of a full turn.</summary>
        public static uint ToAngle(float degrees, int bits)
        {
            if (!MathUtil.IsFinite(degrees)) return 0u;
            float turns = degrees / FullTurnDegrees;
            turns -= System.MathF.Floor(turns);
            long steps = 1L << bits;
            long q = (long)System.MathF.Floor(turns * steps + 0.5f);
            return (uint)(q & (steps - 1));
        }

        public static float FromAngle(uint quantized, int bits) => quantized * (FullTurnDegrees / (1L << bits));

        public static float AngleStep(int bits) => FullTurnDegrees / (1L << bits);

        // ---- Area radius ----

        /// <summary>Radius in cells → u8 in 1/<see cref="NetProtocol.RadiusStepsPerCell"/> cell, clamped.</summary>
        public static byte ToRadius(float cells)
        {
            if (!MathUtil.IsFinite(cells)) return 0;
            int q = MathUtil.RoundToInt(MathUtil.Max(cells, 0f) * NetProtocol.RadiusStepsPerCell);
            return (byte)MathUtil.Clamp(q, 0, RadiusMaxValue);
        }

        public static float FromRadius(byte quantized) => quantized / (float)NetProtocol.RadiusStepsPerCell;
    }
}
