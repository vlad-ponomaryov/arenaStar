using Arena.Core.Math;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Temporary rectangular arena (until the grid map of S2.5 replaces it). Coordinates per D-009: origin (0, 0),
    /// team 0 at small Y.
    /// </summary>
    public struct ArenaBounds
    {
        public Vec2 Min;
        public Vec2 Max;

        public ArenaBounds(Vec2 min, Vec2 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>Rectangle from (0, 0) to (width, height).</summary>
        public static ArenaBounds FromSize(float width, float height) => new ArenaBounds(Vec2.Zero, new Vec2(width, height));

        public float Width => Max.X - Min.X;
        public float Height => Max.Y - Min.Y;
        public Vec2 Center => new Vec2((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f);
        public bool IsValid => Max.X > Min.X && Max.Y > Min.Y;

        public bool Contains(Vec2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

        /// <summary>Clamps the centre of a circle so the whole circle stays inside (centred if the arena is too narrow).</summary>
        public Vec2 ClampCircle(Vec2 p, float radius)
        {
            return new Vec2(ClampAxis(p.X, Min.X + radius, Max.X - radius), ClampAxis(p.Y, Min.Y + radius, Max.Y - radius));
        }

        private static float ClampAxis(float v, float lo, float hi) => lo > hi ? (lo + hi) * 0.5f : MathUtil.Clamp(v, lo, hi);
    }
}
