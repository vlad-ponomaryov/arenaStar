using System.Diagnostics;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
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
    /// A full 2:30 Acorns match of 6 bots on the seeded data (map "Glade", seeded fighters and bots): it ends with a result,
    /// throws nothing, allocates nothing in the steady state, and simulation + bots stay well under the tick budget.
    /// </summary>
    public sealed class BotFullMatchTests
    {
        private const int WarmupTicks = 300;
        private const double SoftBudgetMs = 2.0;

        [TestCase(1)]
        [TestCase(42)]
        public void SixBots_FullMatch_EndsWithResult_NoAllocations_FastTicks(int seed)
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            MatchSetup setup = MatchSetupFactory.Create(db.MatchRunner, defs, null, seed, true);
            var sim = new MatchSimulation(defs, setup);
            var bots = new BotController(ConfigConverter.ToBotBehaviourDef(db.BotBehaviour));
            BotDifficultyDef novice = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Novice));
            BotDifficultyDef easy = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Easy));
            for (int i = 0; i < setup.SlotCount; i++) bots.AddBot(i, setup.Slots[i].Team == 0 ? novice : easy, seed);
            var inputs = TestMatchFactory.Inputs();
            int maxTicks = defs.Acorns.DurationTicks + 1;

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
            }, "steady state of a full match");

            Assert.IsTrue(sim.IsEnded, "the match ends within 2:30");
            int winner = sim.State.Mode.WinnerTeam;
            Assert.IsTrue(winner >= -1 && winner <= 1, "winner team or draw");

            int damage = 0, acorns = 0, kills = 0;
            for (int i = 0; i < setup.SlotCount; i++)
            {
                damage += sim.State.Stats[i].DamageDealt;
                acorns += sim.State.Stats[i].AcornsCollected;
                kills += sim.State.Stats[i].Kills;
            }
            Assert.Greater(damage, 0, "bots fight");
            Assert.Greater(acorns, 0, "bots collect acorns");

            double avgMs = elapsed * 1000.0 / Stopwatch.Frequency / ticks;
            string summary = "Full bot match seed " + seed + ": " + ticks + " ticks, winner " + winner + ", damage " + damage + ", kills " +
                kills + ", acorns " + acorns + ", average tick (simulation + bots) " + avgMs.ToString("0.000") + " ms";
            UnityEngine.Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.Less(avgMs, SoftBudgetMs, "soft budget per tick");
        }

        private static long TimedStep(MatchSimulation sim, BotController bots, PlayerInput[] inputs)
        {
            long start = Stopwatch.GetTimestamp();
            BotTestFactory.Step(sim, bots, inputs);
            return Stopwatch.GetTimestamp() - start;
        }
    }
}
