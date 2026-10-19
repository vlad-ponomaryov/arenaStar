using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Bots
{
    /// <summary>
    /// F1 (D-107): the Normal (Medium) bot вЂ” lead, ult "as the situation demands", sidestepping, focus on the weak вЂ” beats Easy
    /// clearly more often than 50 % on a series of deterministic full Acorns matches on the seeded data (each seed played twice
    /// with the sides swapped, so the map and line-up favour nobody).
    /// </summary>
    public sealed class BotDifficultyMatchTests
    {
        private const int Seeds = 15;
        // D-131: the order of strength with a clear gap — Medium > Easy > Novice (the share of a stronger level, a draw = ½).
        private const float MediumOverEasy = 0.65f;
        private const float EasyOverNovice = 0.8f;
        private const float MediumOverNovice = 0.9f;

        [Test]
        public void Medium_BeatsEasy_ClearlyMoreOftenThanHalf() =>
            AssertStronger(BotDifficulty.Medium, BotDifficulty.Easy, MediumOverEasy);

        [Test]
        public void Easy_BeatsNovice_Clearly() => AssertStronger(BotDifficulty.Easy, BotDifficulty.Novice, EasyOverNovice);

        [Test]
        public void Medium_BeatsNovice_AlmostAlways() =>
            AssertStronger(BotDifficulty.Medium, BotDifficulty.Novice, MediumOverNovice);

        /// <summary>15 seeds × both sides of a full Acorns match between two bot teams; the stronger one's share ≥ min.</summary>
        private static void AssertStronger(BotDifficulty strong, BotDifficulty weak, float minShare)
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            BotBehaviourDef behaviour = ConfigConverter.ToBotBehaviourDef(db.BotBehaviour);
            BotDifficultyDef weakDef = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(weak));
            BotDifficultyDef strongDef = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(strong));
            int wins = 0, losses = 0, draws = 0;
            for (int seed = 1; seed <= Seeds; seed++)
            for (int strongTeam = 0; strongTeam < 2; strongTeam++)
            {
                MatchSetup setup = MatchSetupFactory.Create(db.MatchRunner, defs, null, seed, true);
                var sim = new MatchSimulation(defs, setup);
                var bots = new BotController(behaviour);
                for (int i = 0; i < setup.SlotCount; i++) bots.AddBot(i, setup.Slots[i].Team == strongTeam ? strongDef : weakDef, seed);
                var inputs = TestMatchFactory.Inputs();
                int maxTicks = defs.Acorns.DurationTicks + 1;
                for (int t = 0; t < maxTicks && !sim.IsEnded; t++) BotTestFactory.Step(sim, bots, inputs);
                int winner = sim.State.Mode.WinnerTeam;
                if (winner == strongTeam) wins++;
                else if (winner < 0) draws++;
                else losses++;
            }
            float share = (wins + 0.5f * draws) / (2 * Seeds);
            string summary = strong + " vs " + weak + ": wins " + wins + ", losses " + losses + ", draws " + draws + ", share " +
                share.ToString("0.00");
            UnityEngine.Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.GreaterOrEqual(share, minShare, summary);
        }

        [Test]
        public void LeadPoint_ProjectileOnly_ScaledByFraction()
        {
            var projectile = new AbilityDef { Delivery = DeliveryKind.Projectile, ProjectileSpeed = 10f };
            var cone = new AbilityDef { Delivery = DeliveryKind.MeleeCone };
            var target = new Vec2(5f, 0f);
            var velocity = new Vec2(0f, 2f); // 0.5 s of flight в†’ 1 cell sideways
            Assert.AreEqual(new Vec2(5f, 1f), BotAim.LeadPoint(Vec2.Zero, target, velocity, projectile, 1f));
            Assert.AreEqual(new Vec2(5f, 0.5f), BotAim.LeadPoint(Vec2.Zero, target, velocity, projectile, 0.5f));
            Assert.AreEqual(target, BotAim.LeadPoint(Vec2.Zero, target, velocity, projectile, 0f), "no lead");
            Assert.AreEqual(target, BotAim.LeadPoint(Vec2.Zero, target, velocity, cone, 1f), "not a projectile");
        }

        [Test]
        public void FocusTarget_PrefersTheWeak_WeightZeroIsTheNearest()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            MatchSetup setup = MatchSetupFactory.Create(db.MatchRunner, defs, null, 3, false);
            var sim = new MatchSimulation(defs, setup);
            var state = sim.State;
            int bot = -1, near = -1, far = -1;
            for (int i = 0; i < setup.SlotCount; i++)
            {
                if (setup.Slots[i].Team == 0) { if (bot < 0) bot = i; continue; }
                if (near < 0) near = i;
                else if (far < 0) far = i;
                else state.Fighters[i].Active = false;
            }
            Vec2 p = defs.Map.Center;
            state.Fighters[bot].Position = p;
            state.Fighters[near].Position = p + new Vec2(2f, 0f);
            state.Fighters[far].Position = p + new Vec2(0f, 4f);
            state.Fighters[far].Health = state.Fighters[far].MaxHealth / 4;
            for (int i = 0; i < setup.SlotCount; i++) state.Fighters[i].VisibleToTeams = ~0u;

            Assert.AreEqual(near, Perception.FindFocusTarget(state, bot, 100f, 0f, out _), "weight 0 вЂ” the nearest");
            Assert.AreEqual(near, Perception.FindNearestEnemy(state, bot, 100f, out _));
            Assert.AreEqual(far, Perception.FindFocusTarget(state, bot, 100f, 4f, out float d), "a quarter-health enemy 2 cells further");
            Assert.AreEqual(4f, d, 1e-4f);
        }
    }
}
