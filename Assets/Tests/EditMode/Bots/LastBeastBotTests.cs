using System.Diagnostics;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Bots.Utility;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Net.Content;
using Arena.Net.Diagnostics;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Bots
{
    /// <summary>
    /// Bots in «Last Beast» (Server-Spec §9.2, S3.3): they flee the fog (also shortly before a step that would cover them),
    /// every other fighter is an enemy, and a full match of 10 bots on the seeded royale map ends with one winner within
    /// the time limit, without allocations in the steady state.
    /// </summary>
    public sealed class LastBeastBotTests
    {
        private const int WarmupTicks = 300;
        private const double SoftBudgetMs = 2.0;

        [Test]
        public void Bot_InTheFog_FleesToTheSafeCentre()
        {
            MatchSimulation sim = LastBeastTestFactory.Create(LastBeastTestFactory.Defs(), new Vec2(1.5f, 10.5f), new Vec2(18.5f, 18.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), 0);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, LastBeastTestFactory.FogStart);
            Assert.IsTrue(LastBeastRules.IsInFog(sim.State, sim.State.Fighters[0].Position));

            float startX = sim.State.Fighters[0].Position.X;
            BotTestFactory.Step(sim, bots, inputs);
            Assert.AreEqual(BotAction.FleeFog, bots.GetBrain(0).Action, "first decision");
            for (int i = 0; i < 30; i++) BotTestFactory.Step(sim, bots, inputs);
            Assert.Greater(sim.State.Fighters[0].Position.X, startX + 1f, "runs towards the centre");
        }

        [Test]
        public void Bot_FleesBeforeTheStep_ThatWouldCoverIt()
        {
            MatchSimulation sim = LastBeastTestFactory.Create(LastBeastTestFactory.Defs(), new Vec2(1.5f, 10.5f), new Vec2(18.5f, 18.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), 0);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, LastBeastTestFactory.FogStart - BotTestFactory.FogWarn + BotTestFactory.DecisionInterval);
            Assert.IsFalse(LastBeastRules.IsInFog(sim.State, sim.State.Fighters[0].Position), "no fog yet");
            BotTestFactory.Step(sim, bots, inputs);
            Assert.AreEqual(BotAction.FleeFog, bots.GetBrain(0).Action, "the next step comes within the warning time");
        }

        [Test]
        public void FleeFog_ScoresOnlyUnderFogThreat()
        {
            BotBehaviourDef b = BotTestFactory.Behaviour();
            var calm = new BotSituation { HealthFraction = 1f, HasObjective = true };
            Assert.AreNotEqual(BotAction.FleeFog, UtilityScorer.Choose(in calm, b));
            var threat = new BotSituation { HealthFraction = 0.1f, HasTarget = true, HasObjective = true, FogThreat = true };
            Assert.AreEqual(BotAction.FleeFog, UtilityScorer.Choose(in threat, b), "above retreat and attack");
        }

        [TestCase(1)]
        [TestCase(42)]
        public void TenBots_FullMatch_EndsWithOneWinner_NoAllocations(int seed)
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db.LastBeastMode, "Last Beast is seeded");
            MatchDefs defs = ConfigConverter.ToLastBeastMatchDefs(db, db.LastBeastMaps[0]);
            var policy = new ContentMatchPolicy { FighterLevel = db.MatchRunner.FighterLevel, UnlockAllAbilities = db.MatchRunner.UnlockAllAbilities };
            MatchSetup setup = HeadlessMatch.CreateBotSetup(defs, policy, seed);
            Assert.AreEqual(SimLimits.MaxFighters, setup.SlotCount, "10 fighters");

            var sim = new MatchSimulation(defs, setup);
            var bots = new BotController(ConfigConverter.ToBotBehaviourDef(db.BotBehaviour));
            BotDifficultyDef novice = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Novice));
            BotDifficultyDef easy = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Easy));
            for (int i = 0; i < setup.SlotCount; i++) bots.AddBot(i, i % 2 == 0 ? novice : easy, seed);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            int maxTicks = defs.LastBeast.MaxDurationTicks + 1;

            AllocationAssert.InitializeTypes(typeof(MatchSimulation).Assembly, typeof(BotController).Assembly);
            long elapsed = 0;
            int ticks = 0;
            for (; ticks < WarmupTicks; ticks++) elapsed += TimedStep(sim, bots, inputs);
            AllocationAssert.NoGcAllocations(() =>
            {
                while (!sim.IsEnded && ticks < maxTicks)
                {
                    elapsed += TimedStep(sim, bots, inputs);
                    ticks++;
                }
            }, "steady state of a Last Beast match");

            Assert.IsTrue(sim.IsEnded, "the match ends");
            Assert.LessOrEqual(sim.State.Mode.EndTick, defs.LastBeast.MaxDurationTicks, "not later than the time limit");
            int winners = 0, winner = -1, damage = 0, boxesBroken = 0;
            for (int i = 0; i < setup.SlotCount; i++)
            {
                int place = sim.State.Stats[i].Place;
                Assert.That(place, Is.InRange(1, setup.SlotCount), "slot " + i + " has a place");
                if (place == 1)
                {
                    winners++;
                    winner = i;
                }
                damage += sim.State.Stats[i].DamageDealt;
            }
            Assert.AreEqual(1, winners, "exactly one winner");
            Assert.AreEqual(sim.State.Fighters[winner].Team, sim.State.Mode.WinnerTeam);
            Assert.Greater(damage, 0, "bots fight");
            int boxes = defs.Map.CountTiles(Arena.Core.Map.TileType.BerryBox);
            boxesBroken = boxes - Arena.Core.Sim.Systems.BoxSystem.ActiveCount(sim.State);

            double avgMs = elapsed * 1000.0 / Stopwatch.Frequency / ticks;
            string summary = "Last Beast bot match seed " + seed + ": " + ticks + " ticks, winner slot " + winner + ", alive at the end " +
                LastBeastRules.CountAlive(sim.State) + ", fog step " + sim.State.Mode.FogStep + ", damage " + damage + ", boxes broken " +
                boxesBroken + "/" + boxes + ", average tick (simulation + bots) " + avgMs.ToString("0.000") + " ms";
            UnityEngine.Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.Less(avgMs, SoftBudgetMs, "soft budget per tick");
        }

        [Test]
        public void HeadlessLastBeast_FromThePack_TwoRunsGiveOneHash()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(Arena.Editor.Net.ContentPackExporter.PackPath);
            ContentBundle bundle = ContentPackReader.Read(asset.bytes, true);
            ContentModeMaps maps = bundle.MapsFor(ModeKind.LastBeast);
            Assert.IsNotNull(maps, "Last Beast maps in the pack");
            MatchDefs defs = bundle.CreateMatchDefs(maps.MapIndices[0], ModeKind.LastBeast);
            Assert.IsTrue(defs.NoRespawn);
            BotDifficultyDef novice = bundle.FindBotDifficulty(BotDifficulty.Novice);
            int maxTicks = defs.LastBeast.MaxDurationTicks + 1;
            HeadlessMatchResult a = HeadlessMatch.Run(defs, HeadlessMatch.CreateBotSetup(defs, bundle.Policy, 5), bundle.BotBehaviour, novice, maxTicks);
            HeadlessMatchResult b = HeadlessMatch.Run(defs, HeadlessMatch.CreateBotSetup(defs, bundle.Policy, 5), bundle.BotBehaviour, novice, maxTicks);
            Assert.IsTrue(a.Ended);
            Assert.GreaterOrEqual(a.WinnerTeam, 0, "a winner");
            Assert.AreEqual(a.StateHash, b.StateHash);
            Assert.AreEqual(a.Ticks, b.Ticks);
        }

        private static long TimedStep(MatchSimulation sim, BotController bots, PlayerInput[] inputs)
        {
            long start = Stopwatch.GetTimestamp();
            BotTestFactory.Step(sim, bots, inputs);
            return Stopwatch.GetTimestamp() - start;
        }
    }
}
