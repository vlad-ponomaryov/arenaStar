using System;
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
    /// Performance of the simulation + bots (S2.13, GDD §8.4): 6 bots on the seeded data, 4500 ticks (2:30 at 30 Hz; if a match
    /// ends early, a second pre-built match continues), average and 99th percentile of the tick time written to the log,
    /// zero allocations. The budget of a tick at 30 Hz is 33 ms; the soft limits here are far below it (editor, Mono).
    /// </summary>
    public sealed class SimulationPerformanceTests
    {
        private const int MeasuredTicks = 4500;
        private const int WarmupTicks = 300;
        private const int Seed = 42;
        private const int SecondSeed = 43;
        private const double SoftAverageMs = 2.0;
        private const double SoftP99Ms = 8.0;

        private sealed class Match
        {
            public MatchSimulation Sim;
            public BotController Bots;
        }

        private static Match Create(GameDatabase db, MatchDefs defs, int seed)
        {
            MatchSetup setup = MatchSetupFactory.Create(db.MatchRunner, defs, null, seed, true);
            var sim = new MatchSimulation(defs, setup);
            var bots = new BotController(ConfigConverter.ToBotBehaviourDef(db.BotBehaviour));
            BotDifficultyDef novice = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Novice));
            BotDifficultyDef easy = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Easy));
            for (int i = 0; i < setup.SlotCount; i++) bots.AddBot(i, setup.Slots[i].Team == 0 ? novice : easy, seed);
            return new Match { Sim = sim, Bots = bots };
        }

        [Test]
        public void SixBots_4500Ticks_LogsAverageAndP99_NoAllocations()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            Match first = Create(db, defs, Seed);
            Match second = Create(db, defs, SecondSeed);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            var times = new long[MeasuredTicks];

            AllocationAssert.InitializeTypes(typeof(MatchSimulation).Assembly, typeof(BotController).Assembly);
            // Warm-up on a throwaway match (JIT, first use of every path); the measured matches start fresh.
            Match warm = Create(db, defs, Seed);
            for (int i = 0; i < WarmupTicks; i++) BotTestFactory.Step(warm.Sim, warm.Bots, inputs);

            int switched = 0;
            AllocationAssert.NoGcAllocations(() =>
            {
                Match m = first;
                for (int t = 0; t < MeasuredTicks; t++)
                {
                    if (m.Sim.IsEnded && m == first)
                    {
                        m = second;
                        switched = t;
                    }
                    long start = Stopwatch.GetTimestamp();
                    BotTestFactory.Step(m.Sim, m.Bots, inputs);
                    times[t] = Stopwatch.GetTimestamp() - start;
                }
            }, "4500 ticks of simulation + 6 bots");

            long total = 0;
            for (int i = 0; i < times.Length; i++) total += times[i];
            Array.Sort(times);
            double toMs = 1000.0 / Stopwatch.Frequency;
            double avgMs = total * toMs / MeasuredTicks;
            double p99Ms = times[(int)(MeasuredTicks * 0.99) - 1] * toMs;
            double maxMs = times[MeasuredTicks - 1] * toMs;
            string summary = "Simulation performance (6 bots, " + MeasuredTicks + " ticks" +
                (switched > 0 ? ", second match from tick " + switched : "") + "): average " + avgMs.ToString("0.000") +
                " ms, p99 " + p99Ms.ToString("0.000") + " ms, max " + maxMs.ToString("0.000") + " ms per tick";
            UnityEngine.Debug.Log(summary);
            TestContext.WriteLine(summary);

            Assert.Less(avgMs, SoftAverageMs, "soft average budget per tick");
            Assert.Less(p99Ms, SoftP99Ms, "soft p99 budget per tick");
        }
    }
}
