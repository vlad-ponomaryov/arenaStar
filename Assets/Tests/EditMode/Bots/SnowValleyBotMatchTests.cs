using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
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
    /// Map-SnowValley-LB §5 п.7 (D-140), light version: 10 bot matches (seeds 1–10) on "Snow Valley" end with one winner within the
    /// time limit and no bot asks to move yet stands still next to a blocker for longer than <see cref="MaxStuckSeconds"/> (stuck on a wall).
    /// Duration and boxes broken by 2:20 are logged (the design targets 2:30–3:50 and ≥ 80 %), not asserted: bot balance is PROTO.
    /// </summary>
    public sealed class SnowValleyBotMatchTests
    {
        private const float MaxStuckSeconds = 5f;
        private const float StillCells = 0.05f;
        private const float BoxesCheckSeconds = 140f;

        [Test]
        public void TenSeeds_MatchesEnd_NoBotStuckAtAWall()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            MapConfig map = AssetDatabase.LoadAssetAtPath<MapConfig>(DataSeeder.RoyaleSnowValleyPath);
            Assert.AreSame(map, db.LastBeastMaps[0]);
            MatchDefs defs = ConfigConverter.ToLastBeastMatchDefs(db, map);
            var policy = new ContentMatchPolicy { FighterLevel = db.MatchRunner.FighterLevel, UnlockAllAbilities = db.MatchRunner.UnlockAllAbilities };
            BotDifficultyDef novice = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Novice));
            BotDifficultyDef easy = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Easy));
            int boxes = defs.Map.CountTiles(TileType.BerryBox);
            int stuckLimit = SimTime.SecondsToTicks(MaxStuckSeconds);
            int boxesTick = SimTime.SecondsToTicks(BoxesCheckSeconds);
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
                int ticks = 0, brokenAt140 = -1;
                int maxTicks = defs.LastBeast.MaxDurationTicks + 1;
                while (!sim.IsEnded && ticks < maxTicks)
                {
                    BotTestFactory.Step(sim, bots, inputs);
                    ticks++;
                    if (ticks == boxesTick) brokenAt140 = boxes - BoxSystem.ActiveCount(sim.State);
                    for (int i = 0; i < setup.SlotCount; i++)
                    {
                        ref Arena.Core.State.FighterState f = ref sim.State.Fighters[i];
                        if (!f.Alive) { still[i] = 0; continue; }
                        bool moved = Vec2.SqrDistance(f.Position, last[i]) > StillCells * StillCells;
                        last[i] = f.Position;
                        // Stuck = the bot asks to move but does not, next to a blocker (standing to shoot or hide is not stuck).
                        bool wantsToMove = inputs[i].Move.X * inputs[i].Move.X + inputs[i].Move.Y * inputs[i].Move.Y > StillCells * StillCells;
                        still[i] = moved || !wantsToMove || !NextToBlocker(sim.State.Grid, f.Position) ? 0 : still[i] + 1;
                        if (still[i] > worstStuck)
                        {
                            worstStuck = still[i];
                            worstWhere = "seed " + seed + " slot " + i + " at " + f.Position + " tick " + ticks + " action " + bots.GetBrain(i).Action;
                        }
                    }
                }
                if (brokenAt140 < 0) brokenAt140 = boxes - BoxSystem.ActiveCount(sim.State);
                Assert.IsTrue(sim.IsEnded, "seed " + seed + ": the match ends");
                Assert.LessOrEqual(sim.State.Mode.EndTick, defs.LastBeast.MaxDurationTicks, "seed " + seed + ": within the time limit");
                report.Append("seed ").Append(seed).Append(": ").Append((ticks / (float)SimLimits.TickRate).ToString("0")).Append(" s, boxes by 2:20 ")
                    .Append(brokenAt140).Append('/').Append(boxes).Append(", fog step ").Append(sim.State.Mode.FogStep).Append('\n');
            }
            report.Append("longest still next to a blocker: ").Append((worstStuck / (float)SimLimits.TickRate).ToString("0.0")).Append(" s (")
                .Append(worstWhere).Append(')');
            UnityEngine.Debug.Log("Snow Valley bot matches:\n" + report);
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
