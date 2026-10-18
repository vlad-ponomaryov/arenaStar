using System.Collections.Generic;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Net.Content;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Data
{
    /// <summary>
    /// The seeded Last Beast map "Thicket" and mode (Server-Spec §9.1): 45×45, 10 shared spawns, berry boxes within
    /// <see cref="SimLimits.MaxBoxes"/>, mirrored on both axes, every spawn reaches the centre, the last safe square is open;
    /// shared spawn parsing and the seeded free-for-all setup.
    /// </summary>
    public sealed class RoyaleThicketMapTests
    {
        private static GameDatabase Database()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db);
            return db;
        }

        [Test]
        public void SeededMap_45x45_TenSharedSpawns_BoxesWithinLimit_MirroredOnBothAxes()
        {
            GameDatabase db = Database();
            Assert.AreEqual(1, db.LastBeastMaps.Count);
            MapConfig config = AssetDatabase.LoadAssetAtPath<MapConfig>(DataSeeder.RoyaleThicketPath);
            Assert.AreSame(config, db.LastBeastMaps[0]);
            MapDef map = MapConverter.ToMapDef(config);
            Assert.AreEqual(45, map.Width);
            Assert.AreEqual(45, map.Height);
            Assert.LessOrEqual(map.CellCount, SimLimits.MaxMapCells);
            Assert.AreEqual(10, map.SpawnCount(MapSpawn.SharedTeam));
            Assert.AreEqual(0, map.SpawnCount(0));
            int boxes = map.CountTiles(TileType.BerryBox);
            Assert.That(boxes, Is.InRange(1, SimLimits.MaxBoxes));
            for (int r = 0; r < map.Height; r++)
                for (int c = 0; c < map.Width; c++)
                {
                    Assert.AreEqual(map.Get(c, r), map.Get(map.Width - 1 - c, r), "left-right mirror at " + c + "," + r);
                    Assert.AreEqual(map.Get(c, r), map.Get(c, map.Height - 1 - r), "top-bottom mirror at " + c + "," + r);
                }
        }

        [Test]
        public void SeededMap_SpawnsReachTheCentre_LastSafeSquareIsOpen()
        {
            GameDatabase db = Database();
            MatchDefs defs = ConfigConverter.ToLastBeastMatchDefs(db, db.LastBeastMaps[0]);
            MapDef map = defs.Map;
            TileGrid grid = map.InitialGrid;
            var centre = new GridCell(map.Width / 2, map.Height / 2);
            var seen = new HashSet<GridCell> { centre };
            var queue = new Queue<GridCell>();
            queue.Enqueue(centre);
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
            for (int k = 0; k < map.SpawnCount(MapSpawn.SharedTeam); k++)
            {
                map.TryGetSpawn(MapSpawn.SharedTeam, k, out GridCell s);
                Assert.IsTrue(seen.Contains(s), "spawn " + k + " " + s + " reaches the centre");
            }

            float half = defs.LastBeast.FogMinHalfSize;
            int from = (int)System.MathF.Floor(map.Width * 0.5f - half), to = (int)System.MathF.Ceiling(map.Width * 0.5f + half) - 1;
            for (int r = from; r <= to; r++)
                for (int c = from; c <= to; c++)
                    Assert.IsFalse(grid.BlocksMovement(c, r), "the last safe square is walkable at " + c + "," + r);
        }

        [Test]
        public void SharedSpawnChar_ParsesAsFloorAndSharedSpawn_RulesCountThem()
        {
            MapDef map = MapConverter.FromRows("ffa", new[] { "S..", ".$.", "..S" });
            Assert.AreEqual(2, map.SpawnCount(MapSpawn.SharedTeam));
            Assert.AreEqual(TileType.Floor, map.Get(0, 2));
            Assert.AreEqual(TileType.BerryBox, map.Get(1, 1));
            map.TryGetSpawn(MapSpawn.SharedTeam, 0, out GridCell first);
            Assert.AreEqual(new GridCell(2, 0), first, "reading order from the bottom-left");
            Assert.DoesNotThrow(() => MapConverter.ValidateRules(map, 0, 2, false));
            Assert.Throws<System.ArgumentException>(() => MapConverter.ValidateRules(map, 0, 10, false));
        }

        [Test]
        public void FreeForAllSetup_OneTeamPerSlot_EverySpawnOnce_BySeed()
        {
            GameDatabase db = Database();
            MatchDefs defs = ConfigConverter.ToLastBeastMatchDefs(db, db.LastBeastMaps[0]);
            var policy = new ContentMatchPolicy { FighterLevel = 1, UnlockAllAbilities = true };
            var fighters = new int[SimLimits.MaxFighters];
            for (int i = 0; i < fighters.Length; i++) fighters[i] = i % defs.Fighters.Length;

            MatchSetup a = MatchSetupBuilder.CreateFreeForAll(defs, policy, fighters, 11);
            MatchSetup b = MatchSetupBuilder.CreateFreeForAll(defs, policy, fighters, 11);
            MatchSetup c = MatchSetupBuilder.CreateFreeForAll(defs, policy, fighters, 12);
            var used = new HashSet<string>();
            bool differs = false;
            for (int i = 0; i < a.SlotCount; i++)
            {
                Assert.AreEqual(i, a.Slots[i].Team, "own team per slot");
                Assert.AreEqual(a.Slots[i].SpawnPosition, b.Slots[i].SpawnPosition, "same seed, same spawns");
                Assert.IsTrue(used.Add(a.Slots[i].SpawnPosition.ToString()), "every spawn once");
                if (a.Slots[i].SpawnPosition != c.Slots[i].SpawnPosition) differs = true;
            }
            Assert.IsTrue(differs, "another seed, another order");
            Assert.Throws<System.ArgumentException>(() => MatchSetupBuilder.CreateFreeForAll(defs, policy, new int[11], 1));
        }

        [Test]
        public void ContentBundle_HasLastBeast_WithNoRespawn()
        {
            ContentBundle bundle = ConfigConverter.ToContentBundle(Database());
            Assert.IsNotNull(bundle.LastBeast);
            ContentModeMaps maps = bundle.MapsFor(ModeKind.LastBeast);
            Assert.IsNotNull(maps);
            MatchDefs defs = bundle.CreateMatchDefs(maps.MapIndices[0], ModeKind.LastBeast);
            Assert.IsTrue(defs.NoRespawn);
            Assert.AreSame(bundle.LastBeast, defs.LastBeast);
            Assert.IsFalse(bundle.CreateMatchDefs(bundle.MapsFor(ModeKind.Acorns).MapIndices[0], ModeKind.Acorns).NoRespawn, "Acorns respawns");
        }
    }
}
