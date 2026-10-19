using System.Collections.Generic;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Art;
using Arena.Editor.Data;
using Arena.Gameplay.View;
using Arena.Modes;
using Arena.Net.Content;
using Arena.Net.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Data
{
    /// <summary>
    /// The seeded Acorns map "Snow Valley" (D-136, D-141, Map-SnowValley §6 п.4): the first Acorns map (DefaultMap, AcornsMaps[0]),
    /// 21×31, 3 + 3 spawns, the hollow in the centre, central symmetry, cell counts of §1.3, every walkable cell connected, no 1-cell
    /// pinch (port of <c>pinches</c> of Tools/map_check.py), acorns never scatter into a blocked cell; berry boxes ($, the big gifts)
    /// are unbreakable walls in Acorns while the small gifts (% + g) break; a valid decor layer (with g) and its view, the spruce-hollow.
    /// </summary>
    public sealed class SnowValleyAcornsMapTests
    {
        private const int Width = 21;
        private const int Height = 31;
        private const int Walls = 28;
        private const int Breakables = 32;
        private const int Boxes = 14;
        private const int Bushes = 36;
        private const int Water = 16;
        private const int SmallGifts = 8;
        private const int Lanterns = 4;
        private const int ScatterSamples = 1000;
        private const int ScatterSeed = 12345;
        private const int HollowTriangleBudget = 1000;

        private static GameDatabase Database()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db);
            return db;
        }

        private static MapConfig SnowValley(GameDatabase db)
        {
            MapConfig config = AssetDatabase.LoadAssetAtPath<MapConfig>(DataSeeder.AcornsSnowValleyPath);
            Assert.IsNotNull(config, DataSeeder.AcornsSnowValleyPath);
            Assert.AreSame(config, db.DefaultMap, "Snow Valley is the offline Acorns map");
            Assert.AreSame(config, db.AcornsMaps[0], "and the first of the online rotation");
            return config;
        }

        [Test]
        public void SeededMap_21x31_ThreeSpawnsPerTeam_HollowInCentre_Symmetric_Counts()
        {
            MapConfig config = SnowValley(Database());
            MapDef map = MapConverter.ToMapDef(config);
            Assert.AreEqual(Width, map.Width);
            Assert.AreEqual(Height, map.Height);
            Assert.AreEqual(3, map.SpawnCount(0));
            Assert.AreEqual(3, map.SpawnCount(1));
            Assert.IsTrue(map.HasHollow);
            Assert.AreEqual(new GridCell(10, 15), map.Hollow);
            Assert.IsTrue(MapConverter.CheckSymmetry(config, out _, out string error), error);
            Assert.IsTrue(MapSymmetry.Check(map, out _));
            Assert.AreEqual(Walls, map.CountTiles(TileType.Wall));
            Assert.AreEqual(Breakables, map.CountTiles(TileType.BreakableWall));
            Assert.AreEqual(Boxes, map.CountTiles(TileType.BerryBox));
            Assert.AreEqual(Bushes, map.CountTiles(TileType.Bush));
            Assert.AreEqual(Water, map.CountTiles(TileType.Water));
            for (int k = 0; k < 3; k++)
            {
                map.TryGetSpawn(0, k, out GridCell s);
                Assert.AreEqual(2, s.Row, "team 0 spawns on row 2");
            }
        }

        [Test]
        public void SeededMap_AllWalkableConnected_NoOneCellPinch_AcornsNeverInABlock()
        {
            GameDatabase db = Database();
            SnowValley(db);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
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
            Assert.AreEqual(walkable, Reach(in grid, first).Count, "every walkable cell is reachable");

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

            var policy = new ContentMatchPolicy { FighterLevel = db.MatchRunner.FighterLevel, UnlockAllAbilities = db.MatchRunner.UnlockAllAbilities };
            var sim = new MatchSimulation(defs, HeadlessMatch.CreateBotSetup(defs, policy, ScatterSeed));
            Vec2 centre = defs.Map.Hollow.Center;
            for (int i = 0; i < ScatterSamples; i++)
            {
                Vec2 p = PickupSystem.ScatterPoint(sim.State, centre, defs.Acorns.SpawnScatterRadius, defs.Acorns.ScatterAttempts);
                GridCell cell = GridCell.Of(p);
                Assert.IsFalse(grid.BlocksMovement(cell.Column, cell.Row), "acorn " + i + " at " + p + " is in a blocked cell");
            }
        }

        [Test]
        public void BigGiftsStayInAcorns_SmallGiftsBreak()
        {
            GameDatabase db = Database();
            SnowValley(db);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            var policy = new ContentMatchPolicy { FighterLevel = db.MatchRunner.FighterLevel, UnlockAllAbilities = db.MatchRunner.UnlockAllAbilities };
            var sim = new MatchSimulation(defs, HeadlessMatch.CreateBotSetup(defs, policy, ScatterSeed));
            Assert.AreEqual(0, BoxSystem.ActiveCount(sim.State), "no berry boxes in Acorns");
            var events = new SimEventBuffer();
            TileGrid grid = sim.State.Grid;

            // The south heap under the spruce (9–11, 13): a ram through it (BreakOverlapping, as DashSystem) leaves it whole.
            Assert.AreEqual(TileType.BerryBox, grid.Get(10, 13));
            Assert.AreEqual(0, TileSystem.BreakOverlapping(sim.State, events, new GridCell(10, 13).Center, 1.2f, 0));
            for (int c = 9; c <= 11; c++) Assert.AreEqual(TileType.BerryBox, grid.Get(c, 13), "big gift " + c + ",13");

            // The small gift in front of the base (5, 6) breaks into floor.
            Assert.AreEqual(TileType.BreakableWall, grid.Get(5, 6));
            Assert.AreEqual(1, TileSystem.BreakOverlapping(sim.State, events, new GridCell(5, 6).Center, 0.4f, 0));
            Assert.AreEqual(TileType.Floor, grid.Get(5, 6));
        }

        [Test]
        public void Decor_IsValid_WithSmallGifts_AndTheViewDrawsTheSnowProps()
        {
            MapConfig config = SnowValley(Database());
            Assert.IsNull(MapDecor.Validate(config));
            Assert.IsNotNull(config.Theme, "the snow theme is built (SnowThemeBuilder) and linked");
            Assert.IsTrue(MapDecor.IsAllowed('%', MapDecor.SmallGift));
            Assert.IsFalse(MapDecor.IsAllowed('$', MapDecor.SmallGift));
            Assert.IsFalse(MapDecor.IsAllowed('#', MapDecor.SmallGift));
            int smallGifts = 0, lanterns = 0;
            foreach (string row in config.Decor)
                foreach (char ch in row)
                {
                    if (ch == MapDecor.SmallGift) smallGifts++;
                    if (ch == MapDecor.Lantern) lanterns++;
                }
            Assert.AreEqual(SmallGifts, smallGifts);
            Assert.AreEqual(Lanterns, lanterns);

            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            MapDef def = MapConverter.ToMapDef(config);
            TileGrid grid = def.InitialGrid;
            var root = new GameObject("SnowValleyAcornsMapTests");
            try
            {
                MapView view = MapView.Create(root.transform, catalog, in grid, config);
                Assert.AreSame(config.Theme, view.Theme);
                Assert.AreEqual(Boxes, view.BoxCount);
                Assert.AreEqual(Breakables, view.BreakableCount);
                Assert.AreEqual(0, view.WallBlockCount, "every wall has a decor prop");
                Assert.AreEqual(0, view.ObstaclePropCount);
                Assert.AreEqual(Walls + Breakables + Boxes, view.DecorPropCount);
                Assert.Greater(view.FrameWallCount, 0, "the frame wall with garlands");
                Assert.IsTrue(view.HasTrails, "footprint trails");

                // The small gift (5, 6): a gift mesh of the theme, breaks into a snow pile.
                int gift = grid.IndexOf(5, 6);
                Assert.IsTrue(view.IsBreakableShown(gift));
                view.OnTileBroken(gift);
                Assert.IsFalse(view.IsBreakableShown(gift));
                Assert.IsTrue(view.IsRubbleShown(gift));

                Assert.IsNotNull(config.Theme.Hollow, "the spruce-hollow mesh of the theme");
                Assert.LessOrEqual(config.Theme.Hollow.triangles.Length / 3, HollowTriangleBudget);
                Assert.LessOrEqual(config.Theme.Hollow.bounds.extents.x, 0.5f, "≤ 1 cell at the base");
                GameObject hollow = AcornsModeView.CreateHollow(root.transform, catalog, config.Theme, WorldCoords.ToWorld(def.Hollow.Center));
                Assert.IsNotNull(hollow);
                Assert.AreSame(config.Theme.Hollow, hollow.GetComponent<MeshFilter>().sharedMesh);
                GameObject plain = AcornsModeView.CreateHollow(root.transform, catalog, null, Vector3.zero);
                Assert.IsTrue(plain == null || plain.GetComponent<MeshFilter>() == null || plain.GetComponent<MeshFilter>().sharedMesh != config.Theme.Hollow,
                    "without a theme: the catalog's hollow");
            }
            finally
            {
                Object.DestroyImmediate(root);
                MapShadowMask.Clear();
            }
        }

        /// <summary>4-neighbour flood fill over walkable cells.</summary>
        private static HashSet<GridCell> Reach(in TileGrid grid, GridCell start)
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
                    if (grid.BlocksMovement(n.Column, n.Row) || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            return seen;
        }
    }
}
