using System;
using System.Collections.Generic;
using Arena.Core.Map;

namespace Arena.Data.Conversion
{
    /// <summary>
    /// Text rows of a <see cref="MapConfig"/> → <see cref="MapDef"/> with validation: equal row lengths, known legend
    /// characters, at most one hollow, the required number of spawns per team and of shared spawns, central symmetry
    /// (<see cref="MapSymmetry"/>, not required for free-for-all maps).
    /// Spawn order: team 0 in reading order from the bottom-left (row 0 first, then left to right); team 1 in the mirrored
    /// order (from the top-right), so on a symmetric map the k-th spawns of both teams are mirror images; shared spawns
    /// (<see cref="MapSpawn.SharedTeam"/>) follow in reading order from the bottom-left.
    /// Throws <see cref="ArgumentException"/> with the map id and the offending cell on invalid data. Called at match start.
    /// </summary>
    public static class MapConverter
    {
        public const char FloorChar = '.';
        public const char WallChar = '#';
        public const char BreakableWallChar = '%';
        public const char BushChar = '"';
        public const char WaterChar = '~';
        public const char BerryBoxChar = '$';
        public const char Team0SpawnChar = 'A';
        public const char Team1SpawnChar = 'B';
        public const char HollowChar = 'H';
        public const char SharedSpawnChar = 'S';

        /// <summary>Full conversion with the rules of the config (spawn count, symmetry).</summary>
        public static MapDef ToMapDef(MapConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            MapDef map = FromRows(config.Id, config.Rows);
            ValidateRules(map, config.RequiredSpawnsPerTeam, config.RequiredSharedSpawns, config.RequireCentralSymmetry);
            return map;
        }

        /// <summary>Parses the rows (format checks only: sizes, legend, one hollow, cells walkable).</summary>
        public static MapDef FromRows(string id, IReadOnlyList<string> rows)
        {
            string name = "Map '" + id + "'";
            if (rows == null || rows.Count == 0) throw new ArgumentException(name + ": no rows");
            int height = rows.Count;
            int width = rows[0] == null ? 0 : rows[0].Length;
            if (width == 0) throw new ArgumentException(name + ": row 0 is empty");

            var tiles = new TileType[width * height];
            var team0 = new List<GridCell>();
            var team1 = new List<GridCell>();
            var shared = new List<GridCell>();
            bool hasHollow = false;
            GridCell hollow = default;

            for (int r = 0; r < height; r++)
            {
                // Text row i is map row height − 1 − i (top first).
                string row = rows[height - 1 - r];
                if (row == null || row.Length != width)
                    throw new ArgumentException(name + ": text row " + (height - 1 - r) + " has length " + (row == null ? 0 : row.Length) +
                        ", expected " + width);
                for (int c = 0; c < width; c++)
                {
                    char ch = row[c];
                    var cell = new GridCell(c, r);
                    tiles[r * width + c] = TileOf(ch, name, cell);
                    if (ch == Team0SpawnChar) team0.Add(cell);
                    else if (ch == Team1SpawnChar) team1.Add(cell);
                    else if (ch == SharedSpawnChar) shared.Add(cell);
                    else if (ch == HollowChar)
                    {
                        if (hasHollow) throw new ArgumentException(name + ": second hollow at " + cell + " (first at " + hollow + ")");
                        hasHollow = true;
                        hollow = cell;
                    }
                }
            }

            team1.Reverse();
            var spawns = new MapSpawn[team0.Count + team1.Count + shared.Count];
            for (int i = 0; i < team0.Count; i++) spawns[i] = new MapSpawn(0, team0[i]);
            for (int i = 0; i < team1.Count; i++) spawns[team0.Count + i] = new MapSpawn(1, team1[i]);
            for (int i = 0; i < shared.Count; i++) spawns[team0.Count + team1.Count + i] = new MapSpawn(MapSpawn.SharedTeam, shared[i]);

            var map = new MapDef
            {
                Id = id, Width = width, Height = height, Tiles = tiles, Spawns = spawns, HasHollow = hasHollow, Hollow = hollow
            };
            map.Validate();
            return map;
        }

        /// <summary>Mode rules: <paramref name="spawnsPerTeam"/> spawns for team 0 and team 1, optional central symmetry.</summary>
        public static void ValidateRules(MapDef map, int spawnsPerTeam, bool requireSymmetry) =>
            ValidateRules(map, spawnsPerTeam, map.SpawnCount(MapSpawn.SharedTeam), requireSymmetry);

        /// <summary>Mode rules: team spawns, exactly <paramref name="sharedSpawns"/> shared spawns, optional central symmetry.</summary>
        public static void ValidateRules(MapDef map, int spawnsPerTeam, int sharedSpawns, bool requireSymmetry)
        {
            string name = "Map '" + map.Id + "'";
            for (int team = 0; team < 2; team++)
            {
                int n = map.SpawnCount(team);
                if (n != spawnsPerTeam)
                    throw new ArgumentException(name + ": team " + team + " has " + n + " spawns, expected " + spawnsPerTeam);
            }
            int sharedCount = map.SpawnCount(MapSpawn.SharedTeam);
            if (sharedCount != sharedSpawns)
                throw new ArgumentException(name + ": " + sharedCount + " shared spawns, expected " + sharedSpawns);
            if (requireSymmetry && !MapSymmetry.Check(map, out GridCell mismatch))
                throw new ArgumentException(name + ": not centrally symmetric at cell " + mismatch);
        }

        /// <summary>Symmetry check of a config for the map editor; false (with the cell) also if the rows do not parse.</summary>
        public static bool CheckSymmetry(MapConfig config, out GridCell mismatch, out string error)
        {
            try
            {
                MapDef map = FromRows(config.Id, config.Rows);
                error = null;
                if (MapSymmetry.Check(map, out mismatch)) return true;
                error = "Not centrally symmetric at cell " + mismatch;
                return false;
            }
            catch (ArgumentException e)
            {
                mismatch = default;
                error = e.Message;
                return false;
            }
        }

        public static char CharOf(TileType type)
        {
            switch (type)
            {
                case TileType.Wall: return WallChar;
                case TileType.BreakableWall: return BreakableWallChar;
                case TileType.Bush: return BushChar;
                case TileType.Water: return WaterChar;
                case TileType.BerryBox: return BerryBoxChar;
                default: return FloorChar;
            }
        }

        private static TileType TileOf(char ch, string name, GridCell cell)
        {
            switch (ch)
            {
                case FloorChar:
                case Team0SpawnChar:
                case Team1SpawnChar:
                case HollowChar:
                case SharedSpawnChar:
                    return TileType.Floor;
                case WallChar: return TileType.Wall;
                case BreakableWallChar: return TileType.BreakableWall;
                case BushChar: return TileType.Bush;
                case WaterChar: return TileType.Water;
                case BerryBoxChar: return TileType.BerryBox;
                default: throw new ArgumentException(name + ": unknown character '" + ch + "' at cell " + cell);
            }
        }
    }
}
