namespace Arena.Core.Map
{
    /// <summary>
    /// Central symmetry of team maps (GDD §5.5): cell (c, r) must equal cell (W−1−c, H−1−r); the k-th spawn of team 0
    /// must mirror the k-th spawn of team 1; the hollow must be its own mirror (the centre cell).
    /// Used by MapConverter validation and by the map editor (S2.6).
    /// </summary>
    public static class MapSymmetry
    {
        /// <summary>Mirror of a cell through the map centre.</summary>
        public static GridCell Mirror(GridCell cell, int width, int height) =>
            new GridCell(width - 1 - cell.Column, height - 1 - cell.Row);

        /// <summary>True if the tiles are centrally symmetric; otherwise <paramref name="mismatch"/> is the first differing
        /// cell in row-major order (bottom row first).</summary>
        public static bool CheckTiles(int width, int height, TileType[] tiles, out GridCell mismatch)
        {
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    int mirror = (height - 1 - r) * width + (width - 1 - c);
                    if (tiles[r * width + c] == tiles[mirror]) continue;
                    mismatch = new GridCell(c, r);
                    return false;
                }
            }
            mismatch = default;
            return true;
        }

        /// <summary>Tiles, team 0 ↔ team 1 spawns and the hollow; <paramref name="mismatch"/> is the first offending cell.</summary>
        public static bool Check(MapDef map, out GridCell mismatch)
        {
            if (!CheckTiles(map.Width, map.Height, map.Tiles, out mismatch)) return false;

            int count = map.SpawnCount(0);
            if (map.SpawnCount(1) != count)
            {
                mismatch = FirstSpawnCell(map);
                return false;
            }
            for (int k = 0; k < count; k++)
            {
                map.TryGetSpawn(0, k, out GridCell a);
                map.TryGetSpawn(1, k, out GridCell b);
                if (Mirror(a, map.Width, map.Height) == b) continue;
                mismatch = a;
                return false;
            }
            if (map.HasHollow && Mirror(map.Hollow, map.Width, map.Height) != map.Hollow)
            {
                mismatch = map.Hollow;
                return false;
            }
            mismatch = default;
            return true;
        }

        private static GridCell FirstSpawnCell(MapDef map) => map.Spawns.Length > 0 ? map.Spawns[0].Cell : default;
    }
}
