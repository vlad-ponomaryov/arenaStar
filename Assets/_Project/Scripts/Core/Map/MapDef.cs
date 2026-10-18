using System;
using Arena.Core.Math;
using Arena.Core.Sim;

namespace Arena.Core.Map
{
    /// <summary>
    /// Static map data (GDD §5.5): size, initial tiles, spawn cells per team, the hollow of the Acorns mode. Built from
    /// <c>MapConfig</c> by <c>MapConverter</c> (or by tests); immutable during the match — the mutable copy of the tiles
    /// (broken walls) lives in <c>MatchState.Tiles</c>. Coordinates per D-009: row 0 is the bottom edge (team 0).
    /// </summary>
    public sealed class MapDef
    {
        public string Id;
        public int Width;
        public int Height;

        /// <summary>Row-major tiles, index = row × <see cref="Width"/> + column.</summary>
        public TileType[] Tiles;

        /// <summary>Spawns of all teams; within a team in slot order (the k-th fighter of a team uses its k-th spawn).</summary>
        public MapSpawn[] Spawns = new MapSpawn[0];

        public bool HasHollow;

        /// <summary>Cell of the hollow (acorn source of the Acorns mode, S2.7); valid if <see cref="HasHollow"/>.</summary>
        public GridCell Hollow;

        public int CellCount => Width * Height;

        public Vec2 Size => new Vec2(Width, Height);

        public Vec2 Center => new Vec2(Width * 0.5f, Height * 0.5f);

        /// <summary>View of the initial tiles (read-only use; the match works on its state copy).</summary>
        public TileGrid InitialGrid => new TileGrid(Width, Height, Tiles);

        public TileType Get(int column, int row) => InitialGrid.Get(column, row);

        /// <summary>Number of spawns of <paramref name="team"/>.</summary>
        public int SpawnCount(int team)
        {
            int n = 0;
            for (int i = 0; i < Spawns.Length; i++)
                if (Spawns[i].Team == team) n++;
            return n;
        }

        /// <summary>The <paramref name="index"/>-th spawn cell of <paramref name="team"/>; false if there is none.</summary>
        public bool TryGetSpawn(int team, int index, out GridCell cell)
        {
            int n = 0;
            for (int i = 0; i < Spawns.Length; i++)
            {
                if (Spawns[i].Team != team) continue;
                if (n == index)
                {
                    cell = Spawns[i].Cell;
                    return true;
                }
                n++;
            }
            cell = default;
            return false;
        }

        /// <summary>All-floor map (tests, temporary arenas).</summary>
        public static MapDef CreateEmpty(int width, int height, string id = "empty")
        {
            var map = new MapDef { Id = id, Width = width, Height = height, Tiles = new TileType[width * height] };
            map.Validate();
            return map;
        }

        /// <summary>Format checks: size within <see cref="SimLimits.MaxMapCells"/>, tiles, spawns and hollow inside and walkable.</summary>
        public void Validate()
        {
            string name = "Map '" + Id + "'";
            if (Width < 1 || Height < 1) throw new ArgumentException(name + ": size must be at least 1×1");
            if (Width * Height > SimLimits.MaxMapCells)
                throw new ArgumentException(name + ": " + Width + "×" + Height + " cells > SimLimits.MaxMapCells " + SimLimits.MaxMapCells);
            if (Tiles == null || Tiles.Length != Width * Height)
                throw new ArgumentException(name + ": tiles must have " + (Width * Height) + " entries");
            if (Spawns == null) throw new ArgumentException(name + ": spawns are null");
            TileGrid grid = InitialGrid;
            for (int i = 0; i < Spawns.Length; i++)
            {
                GridCell c = Spawns[i].Cell;
                if (!grid.InBounds(c.Column, c.Row)) throw new ArgumentException(name + ": spawn " + c + " is outside the map");
                if (grid.BlocksMovement(c.Column, c.Row)) throw new ArgumentException(name + ": spawn " + c + " is not walkable");
                if (Spawns[i].Team < 0 || Spawns[i].Team >= SimLimits.MaxTeams)
                    throw new ArgumentException(name + ": spawn " + c + " has team " + Spawns[i].Team + " out of range");
            }
            if (HasHollow && !grid.InBounds(Hollow.Column, Hollow.Row))
                throw new ArgumentException(name + ": hollow " + Hollow + " is outside the map");
        }
    }
}
