using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Data
{
    public sealed class ConfigConverterTests
    {
        private static MatchDefs LoadDefs()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db, "GameDatabase asset is missing — run DataSeeder.SeedAll");
            return ConfigConverter.ToMatchDefs(db);
        }

        [Test]
        public void Pix_FromAsset_HasGddValues()
        {
            MatchDefs defs = LoadDefs();
            FighterDef pix = defs.Fighters[defs.IndexOfFighter("pix")];
            Assert.AreEqual(3400, pix.MaxHealth);
            Assert.AreEqual(2.5f, pix.MoveSpeed, 1e-5f);
            Assert.AreEqual(42, pix.AttackRechargeTicks);
            Assert.AreEqual(8f, pix.AttackRange, 1e-5f);
            Assert.AreEqual(380, pix.AttackDamage);
            Assert.AreEqual("fighter.pix.name", pix.NameKey);
        }

        [TestCase("kora", 2.2f)]
        [TestCase("bruno", 2.5f)]
        [TestCase("vex", 2.8f)]
        public void SpeedClasses_ResolveToGddSpeeds(string id, float speed)
        {
            MatchDefs defs = LoadDefs();
            Assert.AreEqual(speed, defs.Fighters[defs.IndexOfFighter(id)].MoveSpeed, 1e-5f);
        }

        [Test]
        public void Rules_And_Bounds_FromAsset()
        {
            MatchDefs defs = LoadDefs();
            Assert.AreEqual(3, defs.Rules.MaxCharges);
            Assert.AreEqual(90, defs.Rules.RegenDelayTicks);
            Assert.AreEqual(0.12f, defs.Rules.RegenFractionPerSecond, 1e-6f);
            Assert.AreEqual(0.06f, defs.Rules.LevelBonusPerLevel, 1e-6f);
            Assert.AreEqual(21f, defs.Bounds.Width, 1e-5f);
            Assert.AreEqual(31f, defs.Bounds.Height, 1e-5f);
            Assert.AreEqual(5236, defs.Rules.ScaleByLevel(3400, 10), "level 10 = +54 % (GDD §6)");
        }

        [Test]
        public void AllSixFighters_ConvertAndSimulate()
        {
            MatchDefs defs = LoadDefs();
            Assert.AreEqual(6, defs.Fighters.Length);
            var setup = new MatchSetup { Seed = 1, Slots = new PlayerSlotSetup[defs.Fighters.Length] };
            for (int i = 0; i < setup.Slots.Length; i++)
                setup.Slots[i] = new PlayerSlotSetup(i, i % 2, 1, defs.Bounds.Center);
            var sim = new MatchSimulation(defs, setup);
            var inputs = new PlayerInput[SimLimits.MaxFighters];
            for (int t = 0; t < 30; t++) sim.Tick(inputs);
            Assert.AreEqual(30, sim.State.Tick);
        }

        [Test]
        public void MissingRules_Throws()
        {
            var db = ScriptableObject.CreateInstance<GameDatabase>();
            try
            {
                Assert.Throws<ArgumentException>(() => ConfigConverter.ToMatchDefs(db));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(db);
            }
        }
    }
}
