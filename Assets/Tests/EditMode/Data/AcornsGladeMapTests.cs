using System.Collections.Generic;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Data
{
    /// <summary>The seeded Acorns map "Glade" (GDD §5.4–5.5): size, spawns, hollow, symmetry, reachability.</summary>
    public sealed class AcornsGladeMapTests
    {
        private static MapConfig LoadConfig()
        {
            DataSeeder.SeedAll();
            var config = AssetDatabase.LoadAssetAtPath<MapConfig>(DataSeeder.AcornsGladePath);
            Assert.IsNotNull(config, DataSeeder.AcornsGladePath);
            return config;
        }

        [Test]
        public void SeededMap_IsTheDefault_21x31_ThreeSpawnsPerTeam_HollowInCentre()
        {
            MapConfig config = LoadConfig();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.AreSame(config, db.DefaultMap);

            MapDef map = MapConverter.ToMapDef(config);
            Assert.AreEqual(21, map.Width);
            Assert.AreEqual(31, map.Height);
            Assert.AreEqual(3, map.SpawnCount(0));
            Assert.AreEqual(3, map.SpawnCount(1));
            Assert.IsTrue(map.HasHollow);
            Assert.AreEqual(new GridCell(10, 15), map.Hollow);
            for (int k = 0; k < 3; k++)
            {
                map.TryGetSpawn(0, k, out GridCell s);
                Assert.Less(s.Row, map.Height / 2, "team 0 spawns at the bottom");
            }
        }

        [Test]
        public void SeededMap_IsSymmetric_ModifiedIsNot_WithTheCell()
        {
            MapConfig config = LoadConfig();
            Assert.IsTrue(MapConverter.CheckSymmetry(config, out _, out string error), error);
            Assert.IsTrue(MapSymmetry.Check(MapConverter.ToMapDef(config), out _));

            // Text row 25 = map row 5; turn its column 3 into water.
            string[] rows = (string[])config.Rows.Clone();
            char[] chars = rows[25].ToCharArray();
            chars[3] = chars[3] == MapConverter.WaterChar ? MapConverter.FloorChar : MapConverter.WaterChar;
            rows[25] = new string(chars);
            MapDef changed = MapConverter.FromRows("changed", rows);
            Assert.IsFalse(MapSymmetry.Check(changed, out GridCell mismatch));
            Assert.AreEqual(new GridCell(3, 5), mismatch);
        }

        [Test]
        public void SeededMap_HasBushesNearTheCentreOnBothSides_AndEveryKindOfTile()
        {
            MapDef map = MapConverter.ToMapDef(LoadConfig());
            int bottom = 0, top = 0;
            var kinds = new HashSet<TileType>();
            for (int r = 0; r < map.Height; r++)
            {
                for (int c = 0; c < map.Width; c++)
                {
                    TileType t = map.Get(c, r);
                    kinds.Add(t);
                    // "Near the centre": within 3 rows of the middle row.
                    if (t != TileType.Bush || System.Math.Abs(r - map.Height / 2) > 3) continue;
                    if (r < map.Height / 2) bottom++;
                    else if (r > map.Height / 2) top++;
                }
            }
            Assert.Greater(bottom, 0);
            Assert.AreEqual(bottom, top, "equal bushes near the centre on both sides");
            foreach (TileType t in new[] { TileType.Floor, TileType.Wall, TileType.BreakableWall, TileType.Bush, TileType.Water })
                Assert.IsTrue(kinds.Contains(t), t + " is used");
        }

        [Test]
        public void SeededMap_HollowAndAllSpawnsAreConnected()
        {
            MapDef map = MapConverter.ToMapDef(LoadConfig());
            TileGrid grid = map.InitialGrid;
            map.TryGetSpawn(0, 0, out GridCell start);
            var seen = new HashSet<GridCell> { start };
            var queue = new Queue<GridCell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                GridCell c = queue.Dequeue();
                foreach (GridCell n in new[]
                         {
                             new GridCell(c.Column + 1, c.Row), new GridCell(c.Column - 1, c.Row),
                             new GridCell(c.Column, c.Row + 1), new GridCell(c.Column, c.Row - 1)
                         })
                {
                    if (grid.BlocksMovement(n.Column, n.Row) || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            Assert.IsTrue(seen.Contains(map.Hollow), "hollow reachable");
            for (int team = 0; team < 2; team++)
                for (int k = 0; k < 3; k++)
                {
                    map.TryGetSpawn(team, k, out GridCell s);
                    Assert.IsTrue(seen.Contains(s), "spawn " + team + "/" + k + " reachable");
                }
        }

        [Test]
        public void RandomMatchOnSeededMap_FightersNeverEnterWallsOrWater()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            MatchSetup setup = MatchSetupFactory.CreateDefault(db.MatchRunner, defs);
            var sim = new MatchSimulation(defs, setup);
            var inputs = new Arena.Core.Input.PlayerInput[SimLimits.MaxFighters];
            DeterministicRng rng = DeterministicRng.FromSeed(3);
            for (int t = 0; t < 1800; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rng, inputs, setup.SlotCount);
                sim.Tick(inputs);
                for (int i = 0; i < setup.SlotCount; i++)
                {
                    ref var f = ref sim.State.Fighters[i];
                    Assert.IsFalse(GridCollision.OverlapsBlocking(sim.State.Grid, f.Position, defs.Fighters[f.DefIndex].Radius),
                        "tick " + t + ", fighter " + i + " at " + f.Position);
                }
            }
        }
    }
}
