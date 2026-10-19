using System.Collections.Generic;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Art;
using Arena.Editor.Data;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Data
{
    /// <summary>
    /// The seeded Last Beast map "Snow Valley" (D-139, D-140, Map-SnowValley-LB §5 п.5): 45×45, 10 shared spawns, 44 berry boxes
    /// within <see cref="SimLimits.MaxBoxes"/>, mirrored on both axes, every walkable cell connected, no 1-cell pinch (port of
    /// <c>pinches</c> of Tools/map_check.py), the last safe square ≥ 20 walkable and connected around the spruce, a valid decor layer
    /// and its view (spruce, forts, snowmen, gifts; a snow pile after a break).
    /// </summary>
    public sealed class SnowValleyLbMapTests
    {
        private const int Size = 45;
        private const int Spawns = 10;
        private const int Boxes = 44;
        private const int Breakables = 60;
        private const int MinFinalWalkable = 20;

        private static GameDatabase Database()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db);
            return db;
        }

        private static MapConfig SnowValley(GameDatabase db)
        {
            MapConfig config = AssetDatabase.LoadAssetAtPath<MapConfig>(DataSeeder.RoyaleSnowValleyPath);
            Assert.IsNotNull(config);
            Assert.AreSame(config, db.LastBeastMaps[0], "Snow Valley is the offline Last Beast map");
            return config;
        }

        [Test]
        public void SeededMap_45x45_TenSpawns_44Boxes_MirroredOnBothAxes()
        {
            MapDef map = MapConverter.ToMapDef(SnowValley(Database()));
            Assert.AreEqual(Size, map.Width);
            Assert.AreEqual(Size, map.Height);
            Assert.LessOrEqual(map.CellCount, SimLimits.MaxMapCells);
            Assert.AreEqual(Spawns, map.SpawnCount(MapSpawn.SharedTeam));
            Assert.AreEqual(Boxes, map.CountTiles(TileType.BerryBox));
            Assert.LessOrEqual(Boxes, SimLimits.MaxBoxes);
            Assert.AreEqual(Breakables, map.CountTiles(TileType.BreakableWall));
            for (int r = 0; r < map.Height; r++)
                for (int c = 0; c < map.Width; c++)
                {
                    Assert.AreEqual(map.Get(c, r), map.Get(map.Width - 1 - c, r), "left-right mirror at " + c + "," + r);
                    Assert.AreEqual(map.Get(c, r), map.Get(c, map.Height - 1 - r), "top-bottom mirror at " + c + "," + r);
                }
        }

        [Test]
        public void SeededMap_AllWalkableConnected_NoOneCellPinch_FinalSquareAroundTheSpruce()
        {
            GameDatabase db = Database();
            MatchDefs defs = ConfigConverter.ToLastBeastMatchDefs(db, SnowValley(db));
            TileGrid grid = defs.Map.InitialGrid;
            int walkable = 0;
            GridCell first = default;
            for (int r = 0; r < grid.Height; r++)
                for (int c = 0; c < grid.Width; c++)
                    if (!grid.BlocksMovement(c, r))
                    {
                        if (walkable == 0) first = new GridCell(c, r);
                        walkable++;
                    }
            Assert.AreEqual(walkable, Reach(in grid, first, null).Count, "every walkable cell is reachable");

            var pinches = new List<GridCell>();
            for (int r = 0; r < grid.Height; r++)
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.BlocksMovement(c, r)) continue;
                    bool across = grid.BlocksMovement(c - 1, r) && grid.BlocksMovement(c + 1, r);
                    bool along = grid.BlocksMovement(c, r - 1) && grid.BlocksMovement(c, r + 1);
                    if (across || along) pinches.Add(new GridCell(c, r));
                }
            Assert.IsEmpty(pinches, "no walkable cell between two blockers (Bruno r 0.55, bots' A*)");

            float half = defs.LastBeast.FogMinHalfSize;
            int from = (int)System.MathF.Floor(grid.Width * 0.5f - half), to = (int)System.MathF.Ceiling(grid.Width * 0.5f + half) - 1;
            var square = new HashSet<GridCell>();
            for (int r = from; r <= to; r++)
                for (int c = from; c <= to; c++)
                    if (!grid.BlocksMovement(c, r)) square.Add(new GridCell(c, r));
            Assert.GreaterOrEqual(square.Count, MinFinalWalkable, "the last safe square");
            var e = square.GetEnumerator();
            e.MoveNext();
            Assert.AreEqual(square.Count, Reach(in grid, e.Current, square).Count, "the last safe square is connected inside itself");
            Assert.IsTrue(grid.BlocksMovement(grid.Width / 2, grid.Height / 2), "the spruce stands in the centre");
        }

        [Test]
        public void Decor_IsValid_AndTheViewDrawsTheSnowProps()
        {
            MapConfig config = SnowValley(Database());
            Assert.IsNull(MapDecor.Validate(config));
            Assert.AreEqual(config.Rows.Length, config.Decor.Length);
            Assert.IsNotNull(config.Theme, "the snow theme is built (SnowThemeBuilder) and linked");
            Assert.IsFalse(MapDecor.IsAllowed('.', MapDecor.Snowman));
            Assert.IsFalse(MapDecor.IsAllowed('%', MapDecor.Tree));
            Assert.IsTrue(MapDecor.IsAllowed('$', MapDecor.BigGift));

            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            TileGrid grid = MapConverter.ToMapDef(config).InitialGrid;
            var root = new GameObject("SnowValleyLbMapTests");
            try
            {
                MapView view = MapView.Create(root.transform, catalog, in grid, config);
                Assert.AreSame(config.Theme, view.Theme);
                Assert.AreEqual(Boxes, view.BoxCount);
                Assert.AreEqual(Breakables, view.BreakableCount);
                Assert.AreEqual(0, view.WallBlockCount, "every wall has a decor prop");
                Assert.AreEqual(0, view.ObstaclePropCount);
                Assert.AreEqual(0, view.DecorCount, "no meadow flowers on snow");
                Assert.Greater(view.FrameTreeCount, 0);
                // Walls: 97 cells, the 5 of the spruce are one object → 93 + 60 breakables + 44 gifts.
                Assert.AreEqual(97 - 4 + Breakables + Boxes, view.DecorPropCount);

                // The snowman at text row 2, column 16 → map row 42: breaks into a snow pile.
                int snowman = grid.IndexOf(16, grid.Height - 1 - 2);
                Assert.AreEqual(TileType.BreakableWall, grid.Tiles[snowman]);
                Assert.IsTrue(view.IsBreakableShown(snowman));
                Assert.IsFalse(view.IsRubbleShown(snowman));
                view.OnTileBroken(snowman);
                Assert.IsFalse(view.IsBreakableShown(snowman));
                Assert.IsTrue(view.IsRubbleShown(snowman));
            }
            finally
            {
                Object.DestroyImmediate(root);
                MapShadowMask.Clear();
            }
        }

        [Test]
        public void Decor_WrongSizeOrSymbol_IsRejected()
        {
            var map = ScriptableObject.CreateInstance<MapConfig>();
            try
            {
                map.Rows = new[] { "#%$", "..." };
                map.Decor = new[] { "Tm.", "..." };
                Assert.IsNull(MapDecor.Validate(map));
                Assert.AreEqual(MapDecor.Tree, MapDecor.At(map.Decor, 0, 1), "row 1 = the top text row");
                map.Decor = new[] { "Tm." };
                Assert.IsNotNull(MapDecor.Validate(map), "size");
                map.Decor = new[] { "mT.", "..." };
                Assert.IsNotNull(MapDecor.Validate(map), "symbol on the wrong cell");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        /// <summary>4-neighbour flood fill over walkable cells (inside <paramref name="within"/> if given).</summary>
        private static HashSet<GridCell> Reach(in TileGrid grid, GridCell start, HashSet<GridCell> within)
        {
            var seen = new HashSet<GridCell> { start };
            var queue = new Queue<GridCell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                GridCell c = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    var n = new GridCell(c.Column + (d == 0 ? 1 : d == 1 ? -1 : 0), c.Row + (d == 2 ? 1 : d == 3 ? -1 : 0));
                    if (grid.BlocksMovement(n.Column, n.Row) || (within != null && !within.Contains(n)) || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            return seen;
        }
    }
}
