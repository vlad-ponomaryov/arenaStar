using System;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.State;

namespace Arena.Bots
{
    /// <summary>
    /// What a bot may know (D-022, GDD §7): only enemies that its team sees now (<see cref="FighterState.IsVisibleTo"/> —
    /// bushes, invisibility, reveal, D-024) and that are within the view radius (≈ the player's screen). Items on the map
    /// and the map itself are public. Line of fire follows the projectile rules of the tiles. Pure functions.
    /// </summary>
    public static class Perception
    {
        /// <summary>True if <paramref name="botSlot"/> perceives <paramref name="otherSlot"/> as an enemy target.</summary>
        public static bool CanSee(MatchState state, int botSlot, int otherSlot, float viewRadius)
        {
            if (botSlot == otherSlot) return false;
            ref FighterState bot = ref state.Fighters[botSlot];
            ref FighterState other = ref state.Fighters[otherSlot];
            if (!other.Active || !other.Alive || other.Team == bot.Team || !other.IsVisibleTo(bot.Team)) return false;
            return Vec2.SqrDistance(other.Position, bot.Position) <= viewRadius * viewRadius;
        }

        /// <summary>Nearest perceived enemy (ties to the lower slot), or −1; <paramref name="distance"/> is its centre distance.</summary>
        public static int FindNearestEnemy(MatchState state, int botSlot, float viewRadius, out float distance)
        {
            int count = state.Globals.FighterCount;
            Vec2 p = state.Fighters[botSlot].Position;
            int best = -1;
            float bestSq = float.PositiveInfinity;
            for (int j = 0; j < count; j++)
            {
                if (!CanSee(state, botSlot, j, viewRadius)) continue;
                float d = Vec2.SqrDistance(state.Fighters[j].Position, p);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = j;
                }
            }
            distance = best >= 0 ? MathF.Sqrt(bestSq) : 0f;
            return best;
        }

        /// <summary>
        /// Objective point of the mode: Acorns — the nearest acorn on the map, otherwise the hollow; Last Beast — the nearest
        /// berry that is not in the fog, otherwise none (S3.14: the bots break boxes and explore instead of rushing the centre —
    /// the fog brings them together); no mode — the map centre.
        /// False once the match has ended.
        /// </summary>
        public static bool FindObjective(MatchState state, MatchDefs defs, Vec2 from, out Vec2 point) =>
            FindObjective(state, defs, from, 0f, out point);

        /// <param name="berryRadius">Last Beast: only berries within this distance count (≤ 0 — any distance).</param>
        public static bool FindObjective(MatchState state, MatchDefs defs, Vec2 from, float berryRadius, out Vec2 point)
        {
            point = Vec2.Zero;
            if (state.Mode.IsEnded) return false;
            if (defs.Mode == ModeKind.Acorns)
            {
                float bestSq = float.PositiveInfinity;
                bool found = false;
                for (int i = 0; i < state.Pickups.Length; i++)
                {
                    ref PickupState pickup = ref state.Pickups[i];
                    if (!pickup.Active) continue;
                    float d = Vec2.SqrDistance(pickup.Position, from);
                    if (d < bestSq)
                    {
                        bestSq = d;
                        point = pickup.Position;
                        found = true;
                    }
                }
                if (found) return true;
                if (defs.Map.HasHollow)
                {
                    point = defs.Map.Hollow.Center;
                    return true;
                }
            }
            if (defs.Mode == ModeKind.LastBeast)
            {
                float bestSq = berryRadius > 0f ? berryRadius * berryRadius : float.PositiveInfinity;
                bool found = false;
                for (int i = 0; i < state.Pickups.Length; i++)
                {
                    ref PickupState pickup = ref state.Pickups[i];
                    if (!pickup.Active || pickup.Kind != PickupKind.Berry || LastBeastRules.IsInFog(state, pickup.Position)) continue;
                    float d = Vec2.SqrDistance(pickup.Position, from);
                    if (d < bestSq)
                    {
                        bestSq = d;
                        point = pickup.Position;
                        found = true;
                    }
                }
                return found;
            }
            point = defs.Map.Center;
            return true;
        }

        /// <summary>
        /// True if a straight projectile from <paramref name="from"/> reaches <paramref name="to"/>: no projectile-blocking tile
        /// on the segment (grid traversal of every crossed cell, Amanatides–Woo; the shooter's own cell is not checked).
        /// </summary>
        public static bool HasClearShot(in TileGrid grid, Vec2 from, Vec2 to)
        {
            int x = (int)MathF.Floor(from.X);
            int y = (int)MathF.Floor(from.Y);
            int endX = (int)MathF.Floor(to.X);
            int endY = (int)MathF.Floor(to.Y);
            Vec2 d = to - from;
            int stepX = d.X > 0f ? 1 : (d.X < 0f ? -1 : 0);
            int stepY = d.Y > 0f ? 1 : (d.Y < 0f ? -1 : 0);
            float tDeltaX = stepX != 0 ? MathUtil.Abs(1f / d.X) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? MathUtil.Abs(1f / d.Y) : float.PositiveInfinity;
            float tMaxX = stepX > 0 ? (x + 1 - from.X) / d.X : (stepX < 0 ? (from.X - x) / -d.X : float.PositiveInfinity);
            float tMaxY = stepY > 0 ? (y + 1 - from.Y) / d.Y : (stepY < 0 ? (from.Y - y) / -d.Y : float.PositiveInfinity);

            int steps = (endX > x ? endX - x : x - endX) + (endY > y ? endY - y : y - endY);
            for (int i = 0; i < steps; i++)
            {
                if (tMaxX < tMaxY)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                if (grid.BlocksProjectiles(x, y)) return false;
            }
            return true;
        }
    }
}
