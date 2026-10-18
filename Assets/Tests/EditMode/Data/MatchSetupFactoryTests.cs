using System;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Tests.EditMode.Data
{
    public sealed class MatchSetupFactoryTests
    {
        private MatchRunnerConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<MatchRunnerConfig>();
            _config.MaxTicksPerFrame = 4;
            _config.Seed = 7;
            _config.LocalSlot = 0;
            _config.TeamSize = 3;
            _config.FighterLevel = 1;
            _config.SlotFighterIds.AddRange(new[] { "test", "test2", "test", "test2", "test", "test2" });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void Default_3v3_TeamsAndSymmetricSpawns()
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            MatchSetup setup = MatchSetupFactory.CreateDefault(_config, defs);

            Assert.AreEqual(6, setup.SlotCount);
            Assert.AreEqual(7, setup.Seed);
            Vec2 centre = defs.Map.Center;
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(i < 3 ? 0 : 1, setup.Slots[i].Team, "slot " + i);
                Assert.AreEqual(i % 2, setup.Slots[i].FighterIndex);
                Assert.AreEqual(1, setup.Slots[i].Level);
            }
            // Map spawns (cell centres) in order: team 0 at columns 6, 10, 14 of row 2.
            Assert.AreEqual(new Vec2(6.5f, 2.5f), setup.Slots[0].SpawnPosition);
            Assert.AreEqual(new Vec2(10.5f, 2.5f), setup.Slots[1].SpawnPosition);
            Assert.AreEqual(new Vec2(14.5f, 2.5f), setup.Slots[2].SpawnPosition);
            for (int i = 0; i < 3; i++)
            {
                // Central symmetry: slot i of team 0 mirrors slot i of team 1 through the arena centre.
                Vec2 a = setup.Slots[i].SpawnPosition;
                Vec2 b = setup.Slots[i + 3].SpawnPosition;
                Assert.IsTrue(Vec2.Approximately((a + b) * 0.5f, centre, 1e-5f), "pair " + i);
            }

            Assert.DoesNotThrow(() => new MatchSimulation(defs, setup));
        }

        [Test]
        public void MapWithoutEnoughSpawns_Throws()
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            defs.Map = MapDef.CreateEmpty(21, 31);
            Assert.Throws<ArgumentException>(() => MatchSetupFactory.CreateDefault(_config, defs));
        }

        [Test]
        public void UnknownFighterId_OrBadLocalSlot_Throw()
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            _config.SlotFighterIds[2] = "nobody";
            Assert.Throws<ArgumentException>(() => MatchSetupFactory.CreateDefault(_config, defs));

            _config.SlotFighterIds[2] = "test";
            _config.LocalSlot = 6;
            Assert.Throws<ArgumentException>(() => MatchSetupFactory.CreateDefault(_config, defs));
        }

        [Test]
        public void MenuFighter_GoesToLocalSlot_SwappingWithItsSlot_AndSeedIsUsed()
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            MatchSetup setup = MatchSetupFactory.Create(_config, defs, "test2", 99);
            Assert.AreEqual(99, setup.Seed);
            Assert.AreEqual(1, setup.Slots[0].FighterIndex, "local slot plays the chosen fighter");
            Assert.AreEqual(0, setup.Slots[1].FighterIndex, "the slot that had it gets the old local fighter");
            for (int i = 2; i < 6; i++) Assert.AreEqual(i % 2, setup.Slots[i].FighterIndex, "slot " + i + " unchanged");

            MatchSetup same = MatchSetupFactory.Create(_config, defs, "test", 5);
            Assert.AreEqual(0, same.Slots[0].FighterIndex);
            Assert.AreEqual(1, same.Slots[1].FighterIndex);
            Assert.Throws<ArgumentException>(() => MatchSetupFactory.Create(_config, defs, "unknown", 1));
        }

        [Test]
        public void SeededDatabase_BuildsAPlayableDefaultMatch()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db.Camera, "camera config seeded");
            Assert.IsNotNull(db.MatchRunner, "match runner config seeded");
            Assert.AreEqual(DataSeeder.CameraConfigPath, AssetDatabase.GetAssetPath(db.Camera));
            Assert.AreEqual(DataSeeder.MatchRunnerConfigPath, AssetDatabase.GetAssetPath(db.MatchRunner));
            Assert.GreaterOrEqual(db.MatchRunner.MaxTicksPerFrame, 1);

            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            MatchSetup setup = MatchSetupFactory.CreateDefault(db.MatchRunner, defs);
            Assert.AreEqual(6, setup.SlotCount);
            Assert.AreEqual(defs.IndexOfFighter("pix"), setup.Slots[db.MatchRunner.LocalSlot].FighterIndex, "player is Pix");
            Assert.AreEqual(0, setup.Slots[db.MatchRunner.LocalSlot].Team);
            for (int i = 0; i < setup.SlotCount; i++)
            {
                GridCell cell = GridCell.Of(setup.Slots[i].SpawnPosition);
                Assert.IsTrue(defs.Map.InitialGrid.InBounds(cell.Column, cell.Row), "spawn " + i + " inside the map");
                Assert.IsFalse(defs.Map.InitialGrid.BlocksMovement(cell.Column, cell.Row), "spawn " + i + " walkable");
            }
        }
    }
}
