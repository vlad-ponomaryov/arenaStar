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
            Assert.AreEqual(DeliveryKind.Projectile, pix.Attack.Delivery);
            Assert.AreEqual(4, pix.Attack.BurstCount);
            Assert.AreEqual(1, pix.Attack.Effects.Length);
            Assert.AreEqual(EffectKind.Damage, pix.Attack.Effects[0].Kind);
            Assert.AreEqual(TargetFilter.Enemy, pix.Attack.Effects[0].Filter);
            Assert.AreEqual(380, pix.Attack.Effects[0].Value);
            Assert.AreEqual("fighter.pix.name", pix.NameKey);
        }

        [Test]
        public void Bruno_FromAsset_HasCone90()
        {
            MatchDefs defs = LoadDefs();
            FighterDef bruno = defs.Fighters[defs.IndexOfFighter("bruno")];
            Assert.AreEqual(DeliveryKind.MeleeCone, bruno.Attack.Delivery);
            Assert.AreEqual(90f, bruno.Attack.ConeAngleDegrees, 1e-5f);
            Assert.AreEqual(2.5f, bruno.AttackRange, 1e-5f);
            Assert.AreEqual(1200, bruno.Attack.Effects[0].Value);
        }

        [Test]
        public void Pix_FromAsset_HasFullAbilitySet()
        {
            MatchDefs defs = LoadDefs();
            FighterDef pix = defs.Fighters[defs.IndexOfFighter("pix")];
            Assert.IsNotNull(pix.Ult);
            Assert.AreEqual(DeliveryKind.Projectile, pix.Ult.Delivery);
            Assert.AreEqual(10, pix.Ult.ProjectileCount, "fan of 10 drops (GDD §6)");
            Assert.AreEqual(8f, pix.Ult.Range, 1e-5f);
            Assert.AreEqual(EffectKind.Damage, pix.Ult.Effects[0].Kind);
            Assert.AreEqual(400, pix.Ult.Effects[0].Value);
            Assert.AreEqual(EffectKind.Knockback, pix.Ult.Effects[1].Kind);
            Assert.Greater(pix.UltChargeRequired, 0);

            Assert.IsNotNull(pix.Trick);
            Assert.AreEqual(DeliveryKind.Dash, pix.Trick.Delivery);
            Assert.AreEqual(3f, pix.Trick.DashDistance, 1e-5f);
            Assert.IsTrue(pix.Trick.UsesMoveDirection);

            Assert.IsNotNull(pix.Talent);
            Assert.AreEqual(TalentCondition.UltCharged, pix.Talent.Condition);
            Assert.AreEqual(TalentModifier.MoveSpeedMul, pix.Talent.Modifier);
            Assert.AreEqual(10, pix.Talent.Value);
        }

        [Test]
        public void Bruno_FromAsset_HasFullAbilitySet()
        {
            MatchDefs defs = LoadDefs();
            FighterDef bruno = defs.Fighters[defs.IndexOfFighter("bruno")];
            Assert.AreEqual(DeliveryKind.Dash, bruno.Ult.Delivery);
            Assert.AreEqual(6f, bruno.Ult.DashDistance, 1e-5f);
            Assert.IsTrue(bruno.Ult.BreaksWalls);
            Assert.AreEqual(1000, bruno.Ult.Effects[0].Value);
            Assert.AreEqual(EffectKind.Knockback, bruno.Ult.Effects[1].Kind);
            Assert.AreEqual(2f, bruno.Ult.Effects[1].Distance, 1e-5f);
            Assert.AreEqual(EffectKind.Stun, bruno.Ult.Effects[2].Kind);
            Assert.AreEqual(15, bruno.Ult.Effects[2].DurationTicks, "0.5 s");

            Assert.AreEqual(DeliveryKind.SelfBuff, bruno.Trick.Delivery);
            Assert.AreEqual(EffectKind.DamageReduction, bruno.Trick.Effects[0].Kind);
            Assert.AreEqual(40, bruno.Trick.Effects[0].Value);
            Assert.AreEqual(60, bruno.Trick.Effects[0].DurationTicks, "2 s");

            Assert.AreEqual(TalentCondition.HealthBelow, bruno.Talent.Condition);
            Assert.AreEqual(40, bruno.Talent.Param);
            Assert.AreEqual(TalentModifier.DamageMul, bruno.Talent.Modifier);
            Assert.AreEqual(15, bruno.Talent.Value);
        }

        [TestCase("chomp")]
        [TestCase("kora")]
        [TestCase("nova")]
        [TestCase("vex")]
        public void OtherFighters_HaveNoUltTrickTalentYet(string id)
        {
            MatchDefs defs = LoadDefs();
            FighterDef f = defs.Fighters[defs.IndexOfFighter(id)];
            Assert.IsNull(f.Ult, "S2.11–S2.12");
            Assert.IsNull(f.Trick);
            Assert.IsNull(f.Talent);
        }

        [Test]
        public void AbilityRules_FromAsset()
        {
            MatchDefs defs = LoadDefs();
            Assert.AreEqual(0.5f, defs.Rules.UltLossOnDeath, 1e-6f);
            Assert.AreEqual(3, defs.Rules.TrickUses);
            Assert.AreEqual(150, defs.Rules.TrickCooldownTicks, "5 s");
            Assert.AreEqual(5, defs.Rules.TrickUnlockLevel);
            Assert.AreEqual(8, defs.Rules.TalentUnlockLevel);
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
        public void Rules_And_Map_FromAsset()
        {
            MatchDefs defs = LoadDefs();
            Assert.AreEqual(3, defs.Rules.MaxCharges);
            Assert.AreEqual(90, defs.Rules.RegenDelayTicks);
            Assert.AreEqual(90, defs.Rules.RespawnTicks, "respawn 3 s (GDD §5.2)");
            Assert.AreEqual(0.12f, defs.Rules.RegenFractionPerSecond, 1e-6f);
            Assert.AreEqual(0.06f, defs.Rules.LevelBonusPerLevel, 1e-6f);
            Assert.AreEqual(21, defs.Map.Width);
            Assert.AreEqual(31, defs.Map.Height);
            Assert.AreEqual(2f, defs.Rules.BushRevealDistance, 1e-6f, "bush reveal distance 2 cells (GDD §5.2)");
            Assert.AreEqual(30, defs.Rules.RevealTicks, "attack reveals for 1 s (GDD §5.2)");
            Assert.AreEqual(5236, defs.Rules.ScaleByLevel(3400, 10), "level 10 = +54 % (GDD §6)");
        }

        [Test]
        public void AllSixFighters_ConvertAndSimulate()
        {
            MatchDefs defs = LoadDefs();
            Assert.AreEqual(6, defs.Fighters.Length);
            var setup = new MatchSetup { Seed = 1, Slots = new PlayerSlotSetup[defs.Fighters.Length] };
            for (int i = 0; i < setup.Slots.Length; i++)
                setup.Slots[i] = new PlayerSlotSetup(i, i % 2, 1, defs.Map.Hollow.Center);
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
