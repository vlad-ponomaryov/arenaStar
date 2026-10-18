using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Bots.Utility;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Bots
{
    /// <summary>Basic bot behaviour (GDD §7, S2.8): retreat, shooting with spread and reaction delay, charges, bushes, acorns.</summary>
    public sealed class BotControllerTests
    {
        private const int Bot = 0;
        private const int Enemy = 1;

        /// <summary>Bot (team 0, fighter 0 — 8-cell shooter) and a dummy enemy (team 1) on the open 21×31 test map, no mode.</summary>
        private static MatchSimulation Duel(Vec2 bot, Vec2 enemy, MatchDefs defs = null) =>
            TestMatchFactory.CreateWith(defs ?? TestMatchFactory.CreateDefs(),
                TestMatchFactory.Slot(0, 0, bot.X, bot.Y), TestMatchFactory.Slot(0, 1, enemy.X, enemy.Y));

        [Test]
        public void LowHealth_Retreats_TowardsOwnSpawn()
        {
            MatchSimulation sim = Duel(new Vec2(10.5f, 12.5f), new Vec2(10.5f, 28.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), Bot);
            ref FighterState f = ref sim.State.Fighters[Bot];
            f.Health = (int)(f.MaxHealth * 0.3f);
            float startY = f.Position.Y;
            var inputs = TestMatchFactory.Inputs();

            for (int t = 0; t < 30; t++) BotTestFactory.Step(sim, bots, inputs);

            Assert.AreEqual(BotAction.Retreat, bots.GetBrain(Bot).Action);
            Assert.Less(sim.State.Fighters[Bot].Position.Y, startY - 1f, "moves back to the spawns in row 2");
        }

        [Test]
        public void HealthyBot_DoesNotRetreat_RetreatingBotWaitsForRecovery()
        {
            MatchSimulation sim = Duel(new Vec2(10.5f, 12.5f), new Vec2(10.5f, 28.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), Bot);
            var inputs = TestMatchFactory.Inputs();
            for (int t = 0; t < 12; t++) BotTestFactory.Step(sim, bots, inputs);
            Assert.AreEqual(BotAction.MoveToObjective, bots.GetBrain(Bot).Action, "full health: goes to the objective");

            BotBehaviourDef b = BotTestFactory.Behaviour();
            var s = new BotSituation { HealthFraction = 0.5f, Retreating = true, HasObjective = true };
            Assert.AreEqual(BotAction.Retreat, UtilityScorer.Choose(in s, b), "50 % < recover 80 %: keeps retreating");
            s.Retreating = false;
            Assert.AreNotEqual(BotAction.Retreat, UtilityScorer.Choose(in s, b), "50 % ≥ 35 %: no new retreat");
            s.HealthFraction = 0.34f;
            Assert.AreEqual(BotAction.Retreat, UtilityScorer.Choose(in s, b));
            s = new BotSituation { HealthFraction = 1f, HasTarget = true, TargetDistance = 9f, HasObjective = true };
            Assert.AreEqual(BotAction.Attack, UtilityScorer.Choose(in s, b), "a perceived enemy beats the objective");
        }

        [TestCase(BotDifficulty.Novice, BotTestFactory.NoviceSpread)]
        [TestCase(BotDifficulty.Easy, BotTestFactory.EasySpread)]
        public void VisibleEnemyInRange_IsShot_WithinSpread(BotDifficulty difficulty, float spread)
        {
            const int samples = 200;
            MatchSimulation sim = Duel(new Vec2(10.5f, 10.5f), new Vec2(10.5f, 15.5f));
            BotDifficultyDef def = difficulty == BotDifficulty.Novice ? BotTestFactory.Novice() : BotTestFactory.Easy();
            BotController bots = BotTestFactory.Bots(sim, def, Bot);
            var inputs = TestMatchFactory.Inputs();

            int shots = 0;
            float maxDeviation = 0f;
            for (int t = 0; t < 30000 && shots < samples; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, inputs);
                if (inputs[Bot].Has(InputButtons.Attack))
                {
                    Vec2 toEnemy = sim.State.Fighters[Enemy].Position - sim.State.Fighters[Bot].Position;
                    float deviation = MathUtil.AngleBetween(inputs[Bot].Aim, toEnemy);
                    Assert.LessOrEqual(deviation, spread + 1e-3f, "shot " + shots);
                    Assert.IsFalse(inputs[Bot].Has(InputButtons.AutoAim), "bots aim by hand");
                    if (deviation > maxDeviation) maxDeviation = deviation;
                    shots++;
                }
                sim.Tick(inputs);
                ref FighterState e = ref sim.State.Fighters[Enemy];
                e.Health = e.MaxHealth; // a dummy that never falls
            }

            Assert.AreEqual(samples, shots, "the bot keeps shooting at a visible enemy in range");
            Assert.Greater(maxDeviation, spread * 0.6f, "the spread is actually used");
            Assert.Greater(sim.State.Stats[Bot].DamageDealt, 0, "and hits sometimes");
        }

        [Test]
        public void DoesNotShoot_BeforeTheReactionDelay()
        {
            MatchSimulation sim = Duel(new Vec2(10.5f, 10.5f), new Vec2(10.5f, 28.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), Bot);
            var inputs = TestMatchFactory.Inputs();
            for (int t = 0; t < 10; t++) BotTestFactory.Step(sim, bots, inputs);
            Assert.AreEqual(-1, bots.FirstSeenTick(Bot, Enemy), "18 cells away: not perceived");

            // The enemy steps into view 5 cells in front of the bot.
            sim.State.Fighters[Enemy].Position = sim.State.Fighters[Bot].Position + new Vec2(0f, 5f);
            int seenTick = -1, firstShot = -1;
            for (int t = 0; t < 120 && firstShot < 0; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                int tick = sim.State.Tick;
                bots.Update(sim.State, sim.Defs, inputs);
                if (seenTick < 0) seenTick = bots.FirstSeenTick(Bot, Enemy);
                if (inputs[Bot].Has(InputButtons.Attack)) firstShot = tick;
                sim.Tick(inputs);
            }

            Assert.GreaterOrEqual(seenTick, 0, "perceived");
            Assert.GreaterOrEqual(firstShot, seenTick + BotTestFactory.NoviceReaction, "no shot within 600 ms of first seeing");
            Assert.LessOrEqual(firstShot, seenTick + BotTestFactory.NoviceReaction + BotTestFactory.DecisionInterval, "but soon after");
        }

        [Test]
        public void EnemyInBush_IsNotSeen_NorShot()
        {
            MatchDefs bush = BotTestFactory.DefsOnRows(BotTestFactory.RowsWithBlock(21, 21, '"', 9, 14, 11, 16));
            MatchSimulation sim = Duel(new Vec2(10.5f, 10.5f), new Vec2(10.5f, 15.5f), bush);
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), Bot);
            var inputs = TestMatchFactory.Inputs();

            for (int t = 0; t < 90; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, inputs);
                Assert.IsFalse(inputs[Bot].Has(InputButtons.Attack), "tick " + t);
                Assert.AreNotEqual(BotAction.Attack, bots.GetBrain(Bot).Action);
                sim.Tick(inputs);
            }
            Assert.AreEqual(-1, bots.FirstSeenTick(Bot, Enemy));

            // Control: the same enemy out of the bush is attacked.
            MatchSimulation open = Duel(new Vec2(10.5f, 10.5f), new Vec2(10.5f, 15.5f));
            BotController openBots = BotTestFactory.Bots(open, BotTestFactory.Novice(), Bot);
            bool shot = false;
            for (int t = 0; t < 90 && !shot; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                openBots.Update(open.State, open.Defs, inputs);
                shot = inputs[Bot].Has(InputButtons.Attack);
                open.Tick(inputs);
            }
            Assert.IsTrue(shot);
        }

        [Test]
        public void LastCharge_KeptForAHealthyFarEnemy_SpentToFinishOrWhenClose()
        {
            Assert.IsFalse(LastChargeShot(enemyDistance: 5f, enemyHealth: TestMatchFactory.MaxHealth), "far and healthy: keep it");
            Assert.IsTrue(LastChargeShot(enemyDistance: 5f, enemyHealth: 500), "one burst (4 × 380) finishes 500 HP");
            Assert.IsTrue(LastChargeShot(enemyDistance: 2.5f, enemyHealth: TestMatchFactory.MaxHealth), "closer than 3 cells");
        }

        private static bool LastChargeShot(float enemyDistance, int enemyHealth)
        {
            MatchSimulation sim = Duel(new Vec2(10.5f, 10.5f), new Vec2(10.5f, 10.5f + enemyDistance));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), Bot);
            sim.State.Fighters[Bot].Charges = 1;
            sim.State.Fighters[Bot].ChargeProgressTicks = 0; // the next charge comes in 42 ticks
            sim.State.Fighters[Enemy].Health = enemyHealth;
            var inputs = TestMatchFactory.Inputs();
            for (int t = 0; t < 36; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, inputs);
                if (inputs[Bot].Has(InputButtons.Attack)) return true;
                sim.Tick(inputs);
                sim.State.Fighters[Enemy].Position = sim.State.Fighters[Bot].Position + new Vec2(0f, enemyDistance); // keep the distance
            }
            return false;
        }

        [Test]
        public void GoesToTheAcorn_AndPicksItUp()
        {
            MatchSimulation sim = AcornsTestFactory.Create();
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), Bot);
            AcornsTestFactory.Place(sim.State, 0, new Vec2(5.5f, 11.5f));
            var inputs = TestMatchFactory.Inputs();

            for (int t = 0; t < 150 && sim.State.Fighters[Bot].CarriedAcorns == 0; t++) BotTestFactory.Step(sim, bots, inputs);

            Assert.AreEqual(1, sim.State.Fighters[Bot].CarriedAcorns, "walked 6 cells to the acorn");
            Assert.AreEqual(BotAction.MoveToObjective, bots.GetBrain(Bot).Action);
        }

        [Test]
        public void NoAcorns_GoesToTheHollow_AroundWalls()
        {
            MatchDefs defs = AcornsTestFactory.Defs();
            defs.Acorns.SpawnIntervalTicks = 100000; // no acorn falls during the test
            // A wall across the column of the hollow between the bot and the hollow.
            for (int c = 7; c <= 13; c++) defs.Map.Tiles[10 * defs.Map.Width + c] = Arena.Core.Map.TileType.Wall;
            MatchSimulation sim = TestMatchFactory.CreateWith(defs, TestMatchFactory.Slot(0, 0, 10.5f, 5.5f), TestMatchFactory.Slot(0, 1, 15.5f, 28.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), Bot);
            var inputs = TestMatchFactory.Inputs();
            Vec2 hollow = AcornsTestFactory.HollowCell.Center;

            for (int t = 0; t < 300; t++) BotTestFactory.Step(sim, bots, inputs);

            Assert.Less(Vec2.Distance(sim.State.Fighters[Bot].Position, hollow), 1f, "reached the hollow around the wall");
        }

        [Test]
        public void SameSeed_SameBotsMatch()
        {
            MatchState a = RunSixBots(seed: 5, ticks: 900);
            MatchState b = RunSixBots(seed: 5, ticks: 900);
            Assert.IsTrue(a.ContentEquals(b), "bots are deterministic");
            MatchState c = RunSixBots(seed: 6, ticks: 900);
            Assert.IsFalse(a.ContentEquals(c), "another seed plays differently");
        }

        [Test]
        public void SixBots_Update_DoesNotAllocate()
        {
            MatchSimulation sim = SixBotsAcorns(3);
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Easy(), 0, 1, 2, 3, 4, 5);
            var inputs = TestMatchFactory.Inputs();
            for (int t = 0; t < 60; t++) BotTestFactory.Step(sim, bots, inputs);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = 0; t < 600; t++) BotTestFactory.Step(sim, bots, inputs);
            }, "600 ticks of 6 bots");
        }

        private static MatchSimulation SixBotsAcorns(int seed)
        {
            MatchDefs defs = AcornsTestFactory.Defs();
            var setup = new MatchSetup { Seed = seed, Slots = new PlayerSlotSetup[6] };
            for (int i = 0; i < 6; i++)
            {
                int team = i < 3 ? 0 : 1;
                defs.Map.TryGetSpawn(team, i % 3, out Arena.Core.Map.GridCell cell);
                setup.Slots[i] = new PlayerSlotSetup(i % 2, team, 1, cell.Center);
            }
            return new MatchSimulation(defs, setup);
        }

        private static MatchState RunSixBots(int seed, int ticks)
        {
            MatchSimulation sim = SixBotsAcorns(seed);
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Novice(), 0, 1, 2, 3, 4, 5);
            var inputs = TestMatchFactory.Inputs();
            for (int t = 0; t < ticks; t++) BotTestFactory.Step(sim, bots, inputs);
            var copy = new MatchState();
            copy.CopyFrom(sim.State);
            return copy;
        }

        [Test]
        public void BotAim_SpreadsWithinBounds_AndIsReproducible()
        {
            var rng = DeterministicRng.FromSeed(11);
            var rng2 = DeterministicRng.FromSeed(11);
            float min = 0f, max = 0f;
            for (int i = 0; i < 200; i++)
            {
                Vec2 dir = BotAim.Apply(new Vec2(3f, 4f), 25f, ref rng);
                Assert.AreEqual(1f, dir.Length, 1e-4f);
                float signed = MathUtil.DirToAngle(dir) - MathUtil.DirToAngle(new Vec2(3f, 4f));
                Assert.LessOrEqual(MathUtil.Abs(signed), 25f + 1e-3f);
                if (signed < min) min = signed;
                if (signed > max) max = signed;
                Assert.AreEqual(dir, BotAim.Apply(new Vec2(3f, 4f), 25f, ref rng2));
            }
            Assert.Less(min, -15f, "both sides");
            Assert.Greater(max, 15f);
            var r = DeterministicRng.FromSeed(1);
            Assert.AreEqual(new Vec2(0.6f, 0.8f), BotAim.Apply(new Vec2(3f, 4f), 0f, ref r), "no spread: exact");
        }
    }
}
