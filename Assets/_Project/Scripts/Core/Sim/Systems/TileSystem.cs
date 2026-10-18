using System;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Changes of the mutable map (<see cref="MatchState.Tiles"/>): loading the initial tiles at reset and breaking
    /// breakable walls (abilities with <c>BreaksWalls</c>: dashes, wall-breaking projectiles). A broken wall becomes floor
    /// and raises <see cref="SimEventType.TileBroken"/>.
    /// </summary>
    public static class TileSystem
    {
        /// <summary>Copies the map size and initial tiles into the state.</summary>
        public static void Load(MatchState state, MapDef map)
        {
            state.Globals.MapWidth = map.Width;
            state.Globals.MapHeight = map.Height;
            Array.Clear(state.Tiles, 0, state.Tiles.Length);
            Array.Copy(map.Tiles, state.Tiles, map.Tiles.Length);
        }

        /// <summary>Breaks the cell if it is breakable. Returns true if it was broken.</summary>
        public static bool TryBreak(MatchState state, SimEventBuffer events, int column, int row, int source)
        {
            TileGrid grid = state.Grid;
            if (!grid.InBounds(column, row) || !TileRules.IsBreakable(grid.Get(column, row))) return false;
            grid.Set(column, row, TileType.Floor);
            var cell = new GridCell(column, row);
            events.Add(new SimEvent(SimEventType.TileBroken, state.Globals.Tick, source, -1, grid.IndexOf(column, row), cell.Center, Vec2.Zero));
            return true;
        }

        /// <summary>Breaks every breakable cell the circle overlaps. Returns the number broken.</summary>
        public static int BreakOverlapping(MatchState state, SimEventBuffer events, Vec2 p, float radius, int source)
        {
            int c0 = (int)MathF.Floor(p.X - radius), c1 = (int)MathF.Floor(p.X + radius);
            int r0 = (int)MathF.Floor(p.Y - radius), r1 = (int)MathF.Floor(p.Y + radius);
            int broken = 0;
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    if (GridCollision.CircleOverlapsCell(p, radius, c, r) && TryBreak(state, events, c, r, source)) broken++;
            return broken;
        }
    }
}
