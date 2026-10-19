using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
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
    /// Map-SnowValley §6 п.4 (D-141): 10 Acorns bot matches (seeds 1–10, Novice / Easy) on "Snow Valley" (the default map; and on "Glade") end within
    /// 2:30, bots collect acorns, and no bot asks to move yet stands still next to a blocker for longer than
    /// <see cref="MaxStuckSeconds"/> (stuck on a wall, the same check as <see cref="SnowValleyBotMatchTests"/>). Results go to the log.
    /// </summary>
    public sealed class SnowValleyAcornsBotMatchTests
    {
        private const float MaxStuckSeconds = 5f;
        private const float StillCells = 0.05f;

        /// <summary>Snow Valley, and Glade with the same bots for comparison in the log (the fixed line-up — fighters 0–2 against 3–5 —
        /// makes the win rate a line-up result, not a map one).</summary>
        [TestCase(DataSeeder.AcornsSnowValleyPath)]
        [TestCase(DataSeeder.AcornsGladePath)]
        public void TenSeeds_MatchesEnd_BotsCollect_NoBotStuckAtAWall(string mapPath)
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            MapConfig map = AssetDatabase.LoadAssetAtPath<MapConfig>(mapPath);
            Assert.IsNotNull(map, mapPath);
            Assert.AreSame(db.AcornsMaps[0], db.DefaultMap);
            MatchDefs defs = ConfigConverter.ToMatchDefs(db);
            defs.Map = MapConverter.ToMapDef(map);
            defs.Validate();
            var policy = new ContentMatchPolicy { FighterLevel = db.MatchRunner.FighterLevel, UnlockAllAbilities = db.MatchRunner.UnlockAllAbilities };
            BotDifficultyDef novice = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Novice));
            BotDifficultyDef easy = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Easy));
            int stuckLimit = SimTime.SecondsToTicks(MaxStuckSeconds);
            var report = new System.Text.StringBuilder();
            int worstStuck = 0;
            string worstWhere = "";

            for (int seed = 1; seed <= 10; seed++)
            {
                MatchSetup setup = HeadlessMatch.CreateBotSetup(defs, policy, seed);
                var sim = new MatchSimulation(defs, setup);
                var bots = new BotController(ConfigConverter.ToBotBehaviourDef(db.BotBehaviour));
                for (int i = 0; i < setup.SlotCount; i++) bots.AddBot(i, i % 2 == 0 ? novice : easy, seed);
                PlayerInput[] inputs = TestMatchFactory.Inputs();
                var last = new Vec2[setup.SlotCount];
                var still = new int[setup.SlotCount];
                for (int i = 0; i < setup.SlotCount; i++) last[i] = sim.State.Fighters[i].Position;
                int ticks = 0;
                int maxTicks = defs.Acorns.DurationTicks + 1;
                while (!sim.IsEnded && ticks < maxTicks)
                {
                    BotTestFactory.Step(sim, bots, inputs);
                    ticks++;
                    for (int i = 0; i < setup.SlotCount; i++)
                    {
                        ref Arena.Core.State.FighterState f = ref sim.State.Fighters[i];
                        if (!f.Alive) { still[i] = 0; continue; }
                        bool moved = Vec2.SqrDistance(f.Position, last[i]) > StillCells * StillCells;
                        last[i] = f.Position;
                        bool wantsToMove = inputs[i].Move.X * inputs[i].Move.X + inputs[i].Move.Y * inputs[i].Move.Y > StillCells * StillCells;
                        still[i] = moved || !wantsToMove || !NextToBlocker(sim.State.Grid, f.Position) ? 0 : still[i] + 1;
                        if (still[i] > worstStuck)
                        {
                            worstStuck = still[i];
                            worstWhere = "seed " + seed + " slot " + i + " at " + f.Position + " tick " + ticks + " action " + bots.GetBrain(i).Action;
                        }
                    }
                }
                int acorns = 0, kills = 0;
                for (int i = 0; i < setup.SlotCount; i++)
                {
                    acorns += sim.State.Stats[i].AcornsCollected;
                    kills += sim.State.Stats[i].Kills;
                }
                Assert.IsTrue(sim.IsEnded, "seed " + seed + ": the match ends within 2:30");
                Assert.Greater(acorns, 0, "seed " + seed + ": bots collect acorns");
                report.Append("seed ").Append(seed).Append(": ").Append((ticks / (float)SimLimits.TickRate).ToString("0")).Append(" s, winner ")
                    .Append(sim.State.Mode.WinnerTeam).Append(", acorns ").Append(acorns).Append(", kills ").Append(kills).Append('\n');
            }
            report.Append("longest still next to a blocker: ").Append((worstStuck / (float)SimLimits.TickRate).ToString("0.0")).Append(" s (")
                .Append(worstWhere).Append(')');
            UnityEngine.Debug.Log("Acorns bot matches on " + map.Id + ":\n" + report);
            TestContext.WriteLine(report.ToString());
            Assert.LessOrEqual(worstStuck, stuckLimit, "a bot stands still next to a blocker: " + worstWhere);
        }

        /// <summary>True if a cell around the fighter's cell (8 neighbours) blocks movement.</summary>
        private static bool NextToBlocker(in TileGrid grid, Vec2 p)
        {
            int c = (int)System.MathF.Floor(p.X), r = (int)System.MathF.Floor(p.Y);
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                    if ((dc != 0 || dr != 0) && grid.BlocksMovement(c + dc, r + dr)) return true;
            return false;
        }
    }
}
