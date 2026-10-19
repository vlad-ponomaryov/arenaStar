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

        [Test]
        public void Nova_FromAsset_HasGddSet()
        {
            MatchDefs defs = LoadDefs();
            FighterDef nova = defs.Fighters[defs.IndexOfFighter("nova")];
            Assert.AreEqual(2600, nova.MaxHealth);
            Assert.AreEqual(60, nova.AttackRechargeTicks, "2.0 s");
            Assert.AreEqual(DeliveryKind.Projectile, nova.Attack.Delivery);
            Assert.AreEqual(10f, nova.Attack.Range, 1e-5f);
            Assert.AreEqual(1, nova.Attack.PierceCount, "pierces the first enemy");
            Assert.AreEqual(50, nova.Attack.PierceFalloffPercent, "the second takes 50 %");
            Assert.AreEqual(1500, nova.Attack.Effects[0].Value);
            Assert.AreEqual(EffectKind.Mark, nova.Attack.Effects[1].Kind);
            Assert.AreEqual(120, nova.Attack.Effects[1].DurationTicks, "4 s");

            Assert.AreEqual(DeliveryKind.SelfBuff, nova.Ult.Delivery);
            Assert.AreEqual(EffectKind.TeamReveal, nova.Ult.Effects[0].Kind);
            Assert.AreEqual(150, nova.Ult.Effects[0].DurationTicks, "5 s");
            Assert.AreEqual(EffectKind.BoostShots, nova.Ult.Effects[1].Kind);
            Assert.AreEqual(30, nova.Ult.Effects[1].Value);
            Assert.AreEqual(3, nova.Ult.Effects[1].Count);

            Assert.AreEqual(DeliveryKind.SelfArea, nova.Trick.Delivery);
            Assert.AreEqual(2f, nova.Trick.Range, 1e-5f);
            Assert.AreEqual(EffectKind.Knockback, nova.Trick.Effects[0].Kind);
            Assert.AreEqual(3f, nova.Trick.Effects[0].Distance, 1e-5f);

            Assert.AreEqual(TalentCondition.TargetMarked, nova.Talent.Condition);
            Assert.AreEqual(TalentModifier.DamageMul, nova.Talent.Modifier);
            Assert.AreEqual(20, nova.Talent.Value);
            Assert.AreEqual(BotTrickHint.WhenEnemiesClose, nova.BotTrickHint);
        }

        [Test]
        public void Vex_FromAsset_HasGddSet()
        {
            MatchDefs defs = LoadDefs();
            FighterDef vex = defs.Fighters[defs.IndexOfFighter("vex")];
            Assert.AreEqual(3600, vex.MaxHealth);
            Assert.AreEqual(27, vex.AttackRechargeTicks, "0.9 s");
            Assert.AreEqual(DeliveryKind.MeleeCone, vex.Attack.Delivery);
            Assert.AreEqual(1.8f, vex.Attack.Range, 1e-5f);
            Assert.AreEqual(2, vex.Attack.BurstCount, "two strikes");
            Assert.AreEqual(520, vex.Attack.Effects[0].Value);

            Assert.AreEqual(DeliveryKind.Hook, vex.Ult.Delivery);
            Assert.AreEqual(7f, vex.Ult.Range, 1e-5f);
            Assert.Greater(vex.Ult.DashSpeed, 0f, "pull speed");
            Assert.AreEqual(800, vex.Ult.Effects[0].Value);
            Assert.AreEqual(EffectKind.Slow, vex.Ult.Effects[1].Kind);
            Assert.AreEqual(30, vex.Ult.Effects[1].Value);
            Assert.AreEqual(30, vex.Ult.Effects[1].DurationTicks, "1 s");

            Assert.AreEqual(DeliveryKind.SelfBuff, vex.Trick.Delivery);
            Assert.AreEqual(EffectKind.Invisibility, vex.Trick.Effects[0].Kind);
            Assert.AreEqual(45, vex.Trick.Effects[0].DurationTicks, "1.5 s");

            Assert.AreEqual(TalentCondition.OnKill, vex.Talent.Condition);
            Assert.AreEqual(TalentModifier.RefillCharges, vex.Talent.Modifier);
            Assert.AreEqual(BotTrickHint.Engage, vex.BotTrickHint);
        }

        [Test]
        public void Chomp_FromAsset_HasGddSet()
        {
            MatchDefs defs = LoadDefs();
            FighterDef chomp = defs.Fighters[defs.IndexOfFighter("chomp")];
            Assert.AreEqual(3000, chomp.MaxHealth);
            Assert.AreEqual(48, chomp.AttackRechargeTicks, "1.6 s");
            Assert.AreEqual(DeliveryKind.Lob, chomp.Attack.Delivery);
            Assert.AreEqual(7f, chomp.Attack.Range, 1e-5f);
            Assert.AreEqual(1.2f, chomp.Attack.AreaRadius, 1e-5f);
            Assert.AreEqual(15, chomp.Attack.AreaDelayTicks, "zone shown 0.5 s before (GDD 6)");
            Assert.AreEqual(1100, chomp.Attack.Effects[0].Value);

            Assert.AreEqual(DeliveryKind.Projectile, chomp.Ult.Delivery);
            Assert.AreEqual(9f, chomp.Ult.Range, 1e-5f);
            Assert.IsTrue(chomp.Ult.PierceAll);
            Assert.IsTrue(chomp.Ult.BreaksWalls);
            Assert.AreEqual(1600, chomp.Ult.Effects[0].Value);
            Assert.AreEqual(EffectKind.Knockback, chomp.Ult.Effects[1].Kind);
            Assert.AreEqual(2f, chomp.Ult.Effects[1].Distance, 1e-5f);

            Assert.AreEqual(DeliveryKind.PlaceArea, chomp.Trick.Delivery);
            Assert.AreEqual(AreaKind.Mine, chomp.Trick.PlacedArea);
            Assert.AreEqual(300, chomp.Trick.AreaDurationTicks, "10 s");
            Assert.AreEqual(900, chomp.Trick.Effects[0].Value);
            Assert.IsTrue(chomp.Trick.HiddenFromEnemies, "D-030");

            Assert.AreEqual(TalentModifier.AreaRadiusMul, chomp.Talent.Modifier);
            Assert.AreEqual(20, chomp.Talent.Value);
            Assert.AreEqual(BotTrickHint.PlaceAtObjective, chomp.BotTrickHint);
        }

        [Test]
        public void Kora_FromAsset_HasGddSet()
        {
            MatchDefs defs = LoadDefs();
            FighterDef kora = defs.Fighters[defs.IndexOfFighter("kora")];
            Assert.AreEqual(4200, kora.MaxHealth);
            Assert.AreEqual(45, kora.AttackRechargeTicks, "1.5 s");
            Assert.AreEqual(7f, kora.Attack.Range, 1e-5f);
            Assert.AreEqual(2, kora.Attack.BounceCount, "3 targets in all");
            Assert.AreEqual(EffectKind.Damage, kora.Attack.Effects[0].Kind);
            Assert.AreEqual(600, kora.Attack.Effects[0].Value);
            Assert.AreEqual(EffectKind.Heal, kora.Attack.Effects[1].Kind);
            Assert.AreEqual(TargetFilter.Ally, kora.Attack.Effects[1].Filter);
            Assert.AreEqual(500, kora.Attack.Effects[1].Value);

            Assert.AreEqual(DeliveryKind.PlaceArea, kora.Ult.Delivery);
            Assert.AreEqual(AreaKind.Field, kora.Ult.PlacedArea);
            Assert.AreEqual(2.5f, kora.Ult.AreaRadius, 1e-5f);
            Assert.AreEqual(90, kora.Ult.AreaDurationTicks, "3 s");
            Assert.IsTrue(kora.Ult.BlocksEnemyProjectiles);
            Assert.AreEqual(1000, kora.Ult.Effects[0].Value);

            Assert.AreEqual(DeliveryKind.SelfArea, kora.Trick.Delivery);
            Assert.AreEqual(3f, kora.Trick.Range, 1e-5f);
            Assert.AreEqual(1200, kora.Trick.Effects[0].Value);

            Assert.AreEqual(TalentCondition.AllyAura, kora.Talent.Condition);
            Assert.AreEqual(3, kora.Talent.Param);
            Assert.AreEqual(TalentModifier.RegenDelay, kora.Talent.Modifier);
            Assert.AreEqual(60, kora.Talent.Ticks, "2 s instead of 3");
            Assert.AreEqual(BotTrickHint.HealAllies, kora.BotTrickHint);
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
            Assert.IsTrue(defs.Rules.AutoAimPreferClearShot, "tap auto-aim prefers an enemy in the open (D-129)");
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
        public void AcornsMode_FromAsset_HasGddValuesInTicks()
        {
            MatchDefs defs = LoadDefs();
            Assert.AreEqual(Arena.Core.Modes.ModeKind.Acorns, defs.Mode);
            AcornsModeDef a = defs.Acorns;
            Assert.AreEqual(180, a.SpawnIntervalTicks);
            Assert.AreEqual(1800, a.GoldenIntervalTicks);
            Assert.AreEqual(3, a.GoldenValue);
            Assert.AreEqual(12, a.TargetAcorns);
            Assert.AreEqual(360, a.HoldTicks);
            Assert.AreEqual(4500, a.DurationTicks);
            Assert.AreEqual(15, a.PickupDelayTicks);
            Assert.IsTrue(defs.Map.HasHollow);
            Assert.AreEqual(new Arena.Core.Map.GridCell(10, 15), defs.Map.Hollow);
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
