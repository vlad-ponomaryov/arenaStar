using System;
using Arena.Core.Math;

namespace Arena.Core.Map
{
    /// <summary>
    /// Collision against the tile grid (pure, no allocations).
    /// Circles (fighter bodies) move in sub-steps of at most <see cref="MaxSubstep"/> cells; after each sub-step the circle
    /// is pushed out of the most overlapped movement-blocking cell (a few iterations), which removes only the normal
    /// component: the fighter slides along walls and around corners. Cells outside the map are walls.
    /// Rays (projectiles, line of sight) use a grid DDA (Amanatides-Woo) and stop at projectile-blocking cells; water does
    /// not block them.
    /// </summary>
    public static class GridCollision
    {
        /// <summary>Technical tunnelling guard, not a gameplay value: a sub-step shorter than any body radius cannot skip a cell.</summary>
        public const float MaxSubstep = 0.25f;

        /// <summary>Technical: push-out passes per sub-step (a corner touches at most a few cells).</summary>
        public const int ResolveIterations = 4;

        /// <summary>Number of sub-steps for a move of <paramref name="length"/> cells.</summary>
        public static int SubstepCount(float length) => length <= MaxSubstep ? 1 : (int)MathF.Ceiling(length / MaxSubstep);

        /// <summary>Moves a circle by <paramref name="delta"/> with sliding; returns the new centre.</summary>
        public static Vec2 MoveCircle(in TileGrid grid, Vec2 from, Vec2 delta, float radius)
        {
            if (delta.IsZero) return from;
            int steps = SubstepCount(delta.Length);
            Vec2 step = delta / steps;
            Vec2 p = from;
            for (int i = 0; i < steps; i++) p = ResolveCircle(in grid, p + step, radius);
            return p;
        }

        /// <summary>Pushes a circle out of movement-blocking cells (deepest overlap first).</summary>
        public static Vec2 ResolveCircle(in TileGrid grid, Vec2 p, float radius)
        {
            for (int iter = 0; iter < ResolveIterations; iter++)
            {
                int c0 = (int)MathF.Floor(p.X - radius), c1 = (int)MathF.Floor(p.X + radius);
                int r0 = (int)MathF.Floor(p.Y - radius), r1 = (int)MathF.Floor(p.Y + radius);
                float bestDepth = 0f;
                Vec2 bestPush = Vec2.Zero;
                for (int r = r0; r <= r1; r++)
                {
                    for (int c = c0; c <= c1; c++)
                    {
                        if (!grid.BlocksMovement(c, r)) continue;
                        if (!Penetration(p, radius, c, r, out float depth, out Vec2 normal)) continue;
                        if (depth <= bestDepth) continue;
                        bestDepth = depth;
                        bestPush = normal * depth;
                    }
                }
                if (bestDepth <= 0f) break;
                p += bestPush;
            }
            return p;
        }

        /// <summary>True if the circle overlaps any movement-blocking cell deeper than a float tolerance (tests, spawn checks).</summary>
        public static bool OverlapsBlocking(in TileGrid grid, Vec2 p, float radius)
        {
            int c0 = (int)MathF.Floor(p.X - radius), c1 = (int)MathF.Floor(p.X + radius);
            int r0 = (int)MathF.Floor(p.Y - radius), r1 = (int)MathF.Floor(p.Y + radius);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    if (grid.BlocksMovement(c, r) && Penetration(p, radius, c, r, out float depth, out _) && depth > MathUtil.DistanceEpsilon)
                        return true;
            return false;
        }

        /// <summary>True if the circle overlaps cell (column, row).</summary>
        public static bool CircleOverlapsCell(Vec2 p, float radius, int column, int row) =>
            Penetration(p, radius, column, row, out float depth, out _) && depth > 0f;

        /// <summary>Clamps a circle centre into the map rectangle (centred on an axis that is too narrow).</summary>
        public static Vec2 ClampToMap(Vec2 p, float radius, int width, int height) =>
            new Vec2(ClampAxis(p.X, radius, width - radius), ClampAxis(p.Y, radius, height - radius));

        /// <summary>
        /// First projectile-blocking cell along <c>origin + dir * t</c>, 0 &lt;= t &lt;= <paramref name="maxDistance"/>
        /// (<paramref name="dir"/> is a unit vector). <paramref name="distance"/> is where the ray enters that cell
        /// (0 if the origin is inside it).
        /// </summary>
        public static bool Raycast(in TileGrid grid, Vec2 origin, Vec2 dir, float maxDistance, out float distance, out GridCell cell)
        {
            int cx = (int)MathF.Floor(origin.X);
            int cy = (int)MathF.Floor(origin.Y);
            if (grid.BlocksProjectiles(cx, cy))
            {
                distance = 0f;
                cell = new GridCell(cx, cy);
                return true;
            }

            int stepX = dir.X > 0f ? 1 : (dir.X < 0f ? -1 : 0);
            int stepY = dir.Y > 0f ? 1 : (dir.Y < 0f ? -1 : 0);
            float tDeltaX = stepX != 0 ? 1f / MathUtil.Abs(dir.X) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? 1f / MathUtil.Abs(dir.Y) : float.PositiveInfinity;
            float tMaxX = stepX > 0 ? (cx + 1 - origin.X) * tDeltaX : (stepX < 0 ? (origin.X - cx) * tDeltaX : float.PositiveInfinity);
            float tMaxY = stepY > 0 ? (cy + 1 - origin.Y) * tDeltaY : (stepY < 0 ? (origin.Y - cy) * tDeltaY : float.PositiveInfinity);

            while (true)
            {
                float t;
                if (tMaxX < tMaxY)
                {
                    t = tMaxX;
                    cx += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    t = tMaxY;
                    cy += stepY;
                    tMaxY += tDeltaY;
                }
                if (float.IsInfinity(t) || t > maxDistance) break;
                if (!grid.BlocksProjectiles(cx, cy)) continue;
                distance = t;
                cell = new GridCell(cx, cy);
                return true;
            }
            distance = maxDistance;
            cell = default;
            return false;
        }

        /// <summary>True if nothing blocks projectiles on the segment <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static bool HasLineOfSight(in TileGrid grid, Vec2 from, Vec2 to)
        {
            Vec2 d = to - from;
            float length = d.Length;
            if (length <= MathUtil.Epsilon) return !grid.BlocksProjectiles((int)MathF.Floor(from.X), (int)MathF.Floor(from.Y));
            return !Raycast(in grid, from, d / length, length, out _, out _);
        }

        private static bool Penetration(Vec2 p, float radius, int column, int row, out float depth, out Vec2 normal)
        {
            float qx = MathUtil.Clamp(p.X, column, column + 1f);
            float qy = MathUtil.Clamp(p.Y, row, row + 1f);
            float dx = p.X - qx, dy = p.Y - qy;
            float sq = dx * dx + dy * dy;
            if (sq >= radius * radius)
            {
                depth = 0f;
                normal = Vec2.Zero;
                return false;
            }
            if (sq > MathUtil.Epsilon * MathUtil.Epsilon)
            {
                float dist = MathF.Sqrt(sq);
                normal = new Vec2(dx / dist, dy / dist);
                depth = radius - dist;
                return true;
            }
            // Centre inside the cell: leave through the nearest side.
            float left = p.X - column, right = column + 1f - p.X, down = p.Y - row, up = row + 1f - p.Y;
            float min = MathUtil.Min(MathUtil.Min(left, right), MathUtil.Min(down, up));
            if (min == left) normal = new Vec2(-1f, 0f);
            else if (min == right) normal = new Vec2(1f, 0f);
            else if (min == down) normal = new Vec2(0f, -1f);
            else normal = new Vec2(0f, 1f);
            depth = min + radius;
            return true;
        }

        private static float ClampAxis(float v, float lo, float hi) => lo > hi ? (lo + hi) * 0.5f : MathUtil.Clamp(v, lo, hi);
    }
}
