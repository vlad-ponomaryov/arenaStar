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
    /// <summary>
    /// Bot tactics of GDD §7 (S2.9): ult at 2+ enemies or to finish, tricks by the fighter's hint (helmet when focused, roll
    /// to escape), hiding in bushes, checking a bush where an enemy vanished, mistaken shots into empty bushes, the careful
    /// acorns carrier, the per-life retreat shift.
    /// </summary>
    public sealed class BotTacticsTests
    {
        private const int Bot = 0;

        private static MatchSimulation Abilities(MatchDefs defs, params PlayerSlotSetup[] slots) =>
            new MatchSimulation(defs, new MatchSetup { Seed = 1, Slots = slots, UnlockAllAbilities = true });

        private static void Refill(MatchSimulation sim, int slot)
        {
            ref FighterState f = ref sim.State.Fighters[slot];
            if (f.Alive) f.Health = f.MaxHealth;
        }

        // ------------------------------------------------------------------ ult

        private static bool UltUsed(int enemies, int ticks)
        {
            MatchDefs defs = AbilityTestFactory.CreateDefs();
            var slots = new PlayerSlotSetup[1 + enemies];
            slots[0] = AbilityTestFactory.Slot(AbilityTestFactory.Pix, 0, 10.5f, 10.5f);
            for (int i = 0; i < enemies; i++)
                slots[1 + i] = AbilityTestFactory.Slot(AbilityTestFactory.Dummy, 1, 10.5f + (i - (enemies - 1) * 0.5f) * 1.1f, 15.5f);
            MatchSimulation sim = Abilities(defs, slots);
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), Bot);
            AbilityTestFactory.ChargeUlt(sim, Bot);
            var inputs = TestMatchFactory.Inputs();
            bool attacked = false;
            for (int t = 0; t < ticks; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, sim.Events, inputs);
                if (inputs[Bot].Has(InputButtons.Ult)) return true;
                attacked |= inputs[Bot].Has(InputButtons.Attack);
                sim.Tick(inputs);
                for (int i = 1; i <= enemies; i++) Refill(sim, i);
                AbilityTestFactory.ChargeUlt(sim, Bot);
            }
            Assert.IsTrue(attacked, "the bot fought the enemy with its attack");
            return false;
        }

        [Test]
        public void Ult_TwoEnemiesInTheFan_IsUsed()
        {
            Assert.IsTrue(UltUsed(enemies: 2, ticks: 120));
        }

        [Test]
        public void Ult_OneEnemyItDoesNotFinish_IsKept()
        {
            Assert.IsFalse(UltUsed(enemies: 1, ticks: 300), "50000 HP dummy: the fan does not finish it, one enemy is not enough");
        }

        [Test]
        public void UltDecision_WorthIt_ZonesAndDamageEstimate()
        {
            BotBehaviourDef b = BotTestFactory.Behaviour();
            Assert.IsTrue(UltDecision.IsWorthIt(2, 50000, 0, b), "2 enemies in the zone");
            Assert.IsTrue(UltDecision.IsWorthIt(1, 500, 800, b), "finishes the target");
            Assert.IsFalse(UltDecision.IsWorthIt(1, 5000, 800, b));

            FighterDef pix = AbilityTestFactory.CreatePix();
            var rules = AbilityTestFactory.CreateDefs().Rules;
            int far = UltDecision.EstimateDamageOnTarget(pix.Ult, 1, rules, 7f, 0.5f);
            int near = UltDecision.EstimateDamageOnTarget(pix.Ult, 1, rules, 1.5f, 0.5f);
            Assert.GreaterOrEqual(far, AbilityTestFactory.PixUltDamage);
            Assert.Greater(near, far, "more drops of the fan hit a close target");
            Assert.LessOrEqual(near, AbilityTestFactory.PixUltDamage * AbilityTestFactory.PixUltDrops);

            FighterDef bruno = AbilityTestFactory.CreateBruno();
            Vec2 o = new Vec2(10f, 10f);
            Assert.IsTrue(UltDecision.IsInZone(bruno.Ult, o, Vec2.Up, 0.55f, new Vec2(10.6f, 14f), 0.5f), "on the ram's strip");
            Assert.IsFalse(UltDecision.IsInZone(bruno.Ult, o, Vec2.Up, 0.55f, new Vec2(12f, 14f), 0.5f), "beside it");
            Assert.IsFalse(UltDecision.IsInZone(bruno.Ult, o, Vec2.Up, 0.55f, new Vec2(10f, 17.5f), 0.5f), "beyond 6 cells");
            Assert.IsTrue(UltDecision.IsInZone(pix.Ult, o, Vec2.Up, 0.45f, new Vec2(12f, 15f), 0.5f), "inside the 50° fan");
            Assert.IsFalse(UltDecision.IsInZone(pix.Ult, o, Vec2.Up, 0.45f, new Vec2(15f, 12f), 0.5f), "outside the fan");
        }

        [Test]
        public void UltRoll_NoviceRarerThanEasy_AndMisusesSometimes()
        {
            const int samples = 2000;
            int novice = 0, easy = 0, noviceMisuse = 0, carefulMisuse = 0;
            var rngN = DeterministicRng.FromSeed(7);
            var rngE = DeterministicRng.FromSeed(7);
            var rngC = DeterministicRng.FromSeed(7);
            BotDifficultyDef n = BotTestFactory.Novice(), e = BotTestFactory.Easy(), c = BotTestFactory.Careful();
            for (int i = 0; i < samples; i++)
            {
                if (UltDecision.Roll(true, true, n, ref rngN)) novice++;
                if (UltDecision.Roll(true, true, e, ref rngE)) easy++;
                if (UltDecision.Roll(false, true, n, ref rngN)) noviceMisuse++;
                if (UltDecision.Roll(false, true, c, ref rngC)) carefulMisuse++;
            }
            Assert.Less(novice, easy, "Novice uses a good ult less often than Easy");
            Assert.Greater(novice, samples / 10, "but does use it");
            Assert.Greater(noviceMisuse, 0, "Novice sometimes wastes the ult");
            Assert.Less(noviceMisuse, novice);
            Assert.AreEqual(0, carefulMisuse);
        }

        // ------------------------------------------------------------------ tricks

        private static int HelmetUses(bool twoShooters, out bool reducedWhenUsed)
        {
            MatchDefs defs = AbilityTestFactory.CreateDefs();
            defs.Fighters[AbilityTestFactory.Bruno].BotTrickHint = BotTrickHint.WhenFocused;
            MatchSimulation sim = Abilities(defs,
                AbilityTestFactory.Slot(AbilityTestFactory.Bruno, 0, 10.5f, 10.5f),
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 1, 8.5f, 15.5f),
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 1, 12.5f, 15.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), Bot);
            var inputs = TestMatchFactory.Inputs();
            int usesAtStart = sim.State.Fighters[Bot].TrickUsesLeft;
            reducedWhenUsed = false;
            for (int t = 0; t < 150; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, sim.Events, inputs);
                for (int shooter = 1; shooter <= 2; shooter++)
                {
                    if (shooter == 2 && !twoShooters) continue;
                    if ((t + shooter * 10) % 20 != 0) continue;
                    Vec2 aim = (sim.State.Fighters[Bot].Position - sim.State.Fighters[shooter].Position).Normalized;
                    inputs[shooter] = new PlayerInput(Vec2.Zero, aim, InputButtons.Attack);
                }
                int before = sim.State.Fighters[Bot].TrickUsesLeft;
                sim.Tick(inputs);
                if (sim.State.Fighters[Bot].TrickUsesLeft < before) reducedWhenUsed = sim.State.Fighters[Bot].DamageTakenMul < 1f;
                for (int i = 0; i < 3; i++) Refill(sim, i);
            }
            return usesAtStart - sim.State.Fighters[Bot].TrickUsesLeft;
        }

        [Test]
        public void Bruno_UsesHelmet_WhenDamagedByTwoEnemies()
        {
            Assert.GreaterOrEqual(HelmetUses(twoShooters: true, out bool reduced), 1, "helmet under fire of two enemies");
            Assert.IsTrue(reduced, "the helmet reduces damage");
            Assert.AreEqual(0, HelmetUses(twoShooters: false, out _), "one shooter is not 'focused'");
        }

        [Test]
        public void Pix_RollsAway_WhenRetreatingFromACloseEnemy()
        {
            MatchDefs defs = AbilityTestFactory.CreateDefs();
            defs.Fighters[AbilityTestFactory.Pix].BotTrickHint = BotTrickHint.Escape;
            MatchSimulation sim = Abilities(defs,
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 0, 10.5f, 10.5f),
                AbilityTestFactory.Slot(AbilityTestFactory.Dummy, 1, 10.5f, 13.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), Bot);
            sim.State.Fighters[Bot].Health = sim.State.Fighters[Bot].MaxHealth / 4;
            var inputs = TestMatchFactory.Inputs();
            for (int t = 0; t < 60; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, sim.Events, inputs);
                if (inputs[Bot].Has(InputButtons.Trick))
                {
                    Assert.Less(inputs[Bot].Move.Y, -0.9f, "rolls away from the enemy (it is straight up)");
                    return;
                }
                sim.Tick(inputs);
            }
            Assert.Fail("no escape roll within 2 s");
        }

        // ------------------------------------------------------------------ bushes

        [Test]
        public void LowHealth_HidesInANearbyBush()
        {
            MatchDefs defs = BotTestFactory.DefsOnRows(BotTestFactory.RowsWithBlock(21, 21, '"', 14, 9, 16, 11));
            MatchSimulation sim = TestMatchFactory.CreateWith(defs,
                TestMatchFactory.Slot(0, 0, 10.5f, 10.5f), TestMatchFactory.Slot(0, 1, 2.5f, 20.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), Bot);
            sim.State.Fighters[Bot].Health = (int)(sim.State.Fighters[Bot].MaxHealth * 0.3f);
            var inputs = TestMatchFactory.Inputs();

            for (int t = 0; t < 120; t++) BotTestFactory.Step(sim, bots, inputs);

            Assert.AreEqual(BotAction.HideInBush, bots.GetBrain(Bot).Action);
            Assert.IsTrue(sim.State.Fighters[Bot].InBush, "waits for regeneration in the bush");
        }

        [Test]
        public void EnemyVanishesIntoABush_BotChecksTheBushWithAShot()
        {
            MatchDefs defs = BotTestFactory.DefsOnRows(BotTestFactory.RowsWithBlock(21, 21, '"', 7, 14, 10, 16));
            const int enemy = 1;
            MatchSimulation sim = TestMatchFactory.CreateWith(defs,
                TestMatchFactory.Slot(0, 0, 10.5f, 10.5f), TestMatchFactory.Slot(0, 1, 13.5f, 15.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), Bot);
            var inputs = TestMatchFactory.Inputs();

            int hiddenTick = -1;
            bool checking = false;
            for (int t = 0; t < 240; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                int tick = sim.State.Tick;
                bots.Update(sim.State, sim.Defs, sim.Events, inputs);
                checking |= bots.GetBrain(Bot).Action == BotAction.CheckBush;
                bool hidden = !sim.State.Fighters[enemy].IsVisibleTo(0);
                if (hidden && hiddenTick < 0) hiddenTick = tick;
                if (hidden && inputs[Bot].Has(InputButtons.Attack))
                {
                    Assert.IsTrue(checking, "the shot is a bush check");
                    Assert.LessOrEqual(tick - hiddenTick, BotTestFactory.MemoryTicks, "within the memory time");
                    Vec2 toBush = new Vec2(9.5f, 15.5f) - sim.State.Fighters[Bot].Position;
                    Assert.Less(MathUtil.AngleBetween(inputs[Bot].Aim, toBush), BotTestFactory.EasySpread + 15f, "aimed at the bush");
                    return;
                }
                if (sim.State.Fighters[enemy].Position.X > 9f) inputs[enemy] = new PlayerInput(new Vec2(-1f, 0f), Vec2.Zero, InputButtons.None);
                sim.Tick(inputs);
                Refill(sim, Bot);
                Refill(sim, enemy);
            }
            Assert.Greater(hiddenTick, 0, "the enemy hid in the bush");
            Assert.Fail("the bot did not check the bush");
        }

        private static int EmptyBushShots(float chance)
        {
            MatchDefs defs = BotTestFactory.DefsOnRows(BotTestFactory.RowsWithBlock(21, 21, '"', 8, 13, 12, 15));
            MatchSimulation sim = TestMatchFactory.CreateWith(defs,
                TestMatchFactory.Slot(0, 0, 10.5f, 9.5f), TestMatchFactory.Slot(0, 1, 1.5f, 20.5f));
            BotDifficultyDef d = BotTestFactory.Careful();
            d.EmptyBushShotChance = chance;
            BotController bots = BotTestFactory.Bots(sim, d, Bot);
            var inputs = TestMatchFactory.Inputs();
            int shots = 0;
            for (int t = 0; t < 90; t++)
            {
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, sim.Events, inputs);
                if (inputs[Bot].Has(InputButtons.Attack)) shots++;
                sim.Tick(inputs);
                // Keep the bot out of the bush (it walks to the map centre inside the block).
                if (sim.State.Fighters[Bot].Position.Y > 11.5f) sim.State.Fighters[Bot].Position = new Vec2(10.5f, 9.5f);
            }
            Assert.AreEqual(-1, bots.FirstSeenTick(Bot, 1), "nobody to see");
            return shots;
        }

        [Test]
        public void Mistake_ShootsIntoAnEmptyBush_OnlyWithTheChance()
        {
            Assert.Greater(EmptyBushShots(1f), 0, "always-mistaken bot shoots into the bush");
            Assert.AreEqual(0, EmptyBushShots(0f), "no chance, no shots at nothing");
        }

        // ------------------------------------------------------------------ acorns carrier, retreat shift

        private static float AverageAllyDistance(int carried, out BotAction action)
        {
            MatchDefs defs = AcornsTestFactory.Defs();
            defs.Acorns.SpawnIntervalTicks = 100000; // no acorns: the objective is the hollow
            MatchSimulation sim = TestMatchFactory.CreateWith(defs,
                TestMatchFactory.Slot(0, 0, 5.5f, 5.5f), TestMatchFactory.Slot(0, 0, 5.5f, 8.5f), TestMatchFactory.Slot(0, 1, 15.5f, 28.5f));
            BotController bots = BotTestFactory.Bots(sim, BotTestFactory.Careful(), Bot);
            sim.State.Fighters[Bot].CarriedAcorns = carried;
            var inputs = TestMatchFactory.Inputs();
            float sum = 0f;
            const int ticks = 150;
            for (int t = 0; t < ticks; t++)
            {
                BotTestFactory.Step(sim, bots, inputs);
                sum += Vec2.Distance(sim.State.Fighters[Bot].Position, sim.State.Fighters[1].Position);
            }
            action = bots.GetBrain(Bot).Action;
            return sum / ticks;
        }

        [Test]
        public void AcornsCarrier_StaysCloserToTheTeam()
        {
            float free = AverageAllyDistance(0, out BotAction freeAction);
            float carrier = AverageAllyDistance(BotTestFactory.CarrierAcorns, out BotAction carrierAction);
            Assert.AreEqual(BotAction.MoveToObjective, freeAction, "without acorns: to the hollow");
            Assert.AreEqual(BotAction.StayWithTeam, carrierAction);
            Assert.Less(carrier, free, "the carrier keeps closer to its ally");
            Assert.Less(carrier, 3f);

            BotBehaviourDef b = BotTestFactory.Behaviour();
            var s = new BotSituation { HealthFraction = 0.5f, HasObjective = true, IsCarrier = true };
            Assert.IsTrue(UtilityScorer.WantsRetreat(in s, b), "a carrier retreats below 60 %");
            s.IsCarrier = false;
            Assert.IsFalse(UtilityScorer.WantsRetreat(in s, b));
        }

        [Test]
        public void RetreatThreshold_ShiftedPerBot_WithinTheJitter()
        {
            float min = 1f, max = -1f;
            for (int seed = 0; seed < 40; seed++)
            {
                var bots = new BotController(BotTestFactory.Behaviour());
                bots.AddBot(Bot, BotTestFactory.Novice(), seed);
                float shift = bots.GetBrain(Bot).RetreatShift;
                Assert.LessOrEqual(MathUtil.Abs(shift), 0.12f + 1e-5f);
                min = MathUtil.Min(min, shift);
                max = MathUtil.Max(max, shift);
            }
            Assert.Less(min, -0.03f, "sometimes retreats late");
            Assert.Greater(max, 0.03f);
            var careful = new BotController(BotTestFactory.Behaviour());
            careful.AddBot(Bot, BotTestFactory.Careful(), 1);
            Assert.AreEqual(0f, careful.GetBrain(Bot).RetreatShift);

            var low = new BotSituation { HealthFraction = 0.3f, RetreatShift = -0.1f };
            Assert.IsFalse(UtilityScorer.WantsRetreat(in low, BotTestFactory.Behaviour()), "shifted to 25 %: not yet");
        }
    }
}
