using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>No GC allocations in the tick (CLAUDE.md §1, D-033, D-047).</summary>
    public sealed class AllocationTests
    {
        private const int Fighters = 6;
        private const int WarmupTicks = 30;
        private const int MeasuredTicks = 300;

        private static object _sink;

        [Test]
        public void AllocationCheck_DetectsAllocations()
        {
            AllocationAssert.Allocates(() => _sink = new byte[16], "the zero-allocation checks would be meaningless otherwise");
        }

        [Test]
        public void Tick_DoesNotAllocate()
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(Fighters, 4));
            var inputs = TestMatchFactory.Inputs();
            var rng = DeterministicRng.FromSeed(2);

            for (int t = 0; t < WarmupTicks; t++) Step(sim, inputs, ref rng, t);

            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = 0; t < MeasuredTicks; t++) Step(sim, inputs, ref rng, t);
            }, MeasuredTicks + " ticks");
            Assert.AreEqual(WarmupTicks + MeasuredTicks, sim.State.Tick);
        }

        [Test]
        public void Tick_OnMapWithWallsWaterBushes_DoesNotAllocate()
        {
            var defs = TestMatchFactory.CreateDefsOnMap(
                "............",
                "..##..\"\"..~.",
                "..%...\"\"..~.",
                "......#.....",
                ".~~...#..%%.",
                "..\"\"........",
                "............");
            defs.Fighters[0].Attack.BreaksWalls = true;
            var sim = TestMatchFactory.CreateWith(defs,
                TestMatchFactory.Slot(0, 0, 1.5f, 0.5f), TestMatchFactory.Slot(1, 1, 10.5f, 6.5f), TestMatchFactory.Slot(0, 0, 5.5f, 0.5f),
                TestMatchFactory.Slot(1, 1, 3.5f, 6.5f), TestMatchFactory.Slot(0, 0, 8.5f, 0.5f), TestMatchFactory.Slot(1, 1, 0.5f, 6.5f));
            var inputs = TestMatchFactory.Inputs();
            var rng = DeterministicRng.FromSeed(4);

            for (int t = 0; t < WarmupTicks; t++) Step(sim, inputs, ref rng, t);

            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = 0; t < MeasuredTicks; t++) Step(sim, inputs, ref rng, t);
            }, MeasuredTicks + " ticks on a map");
        }

        [Test]
        public void Tick_WithShootingDeathsAndRespawns_DoesNotAllocate()
        {
            var defs = TestMatchFactory.CreateDefs();
            var sim = TestMatchFactory.CreateWith(defs,
                TestMatchFactory.Slot(0, 0, 8f, 12f), TestMatchFactory.Slot(1, 0, 10f, 14f), TestMatchFactory.Slot(0, 0, 12f, 12f),
                TestMatchFactory.Slot(0, 1, 8f, 17f), TestMatchFactory.Slot(1, 1, 10f, 15.5f), TestMatchFactory.Slot(0, 1, 12f, 17f));
            var inputs = TestMatchFactory.Inputs();

            for (int t = 0; t < WarmupTicks; t++) CombatStep(sim, inputs, t);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = WarmupTicks; t < WarmupTicks + CombatTicks; t++) CombatStep(sim, inputs, t);
            }, CombatTicks + " combat ticks");

            int deaths = 0;
            for (int i = 0; i < Fighters; i++) deaths += sim.State.Stats[i].Deaths;
            Assert.Greater(deaths, 0, "the measured ticks must include defeats and respawns");
        }

        private const int CombatTicks = 900;

        private static void CombatStep(MatchSimulation sim, PlayerInput[] inputs, int t)
        {
            // Everyone taps auto-aim attack every other tick (rising edge each time).
            InputButtons buttons = t % 2 == 0 ? InputButtons.Attack | InputButtons.AutoAim : InputButtons.None;
            for (int i = 0; i < Fighters; i++) inputs[i] = new PlayerInput(Vec2.Zero, Vec2.Zero, buttons);
            sim.Tick(inputs);
        }

        [Test]
        public void Tick_WithUltsTricksDashesAndStatuses_DoesNotAllocate()
        {
            var sim = AbilityTestFactory.Create(
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 0, 8f, 12f), AbilityTestFactory.Slot(AbilityTestFactory.Bruno, 0, 10f, 13f),
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 0, 12f, 12f), AbilityTestFactory.Slot(AbilityTestFactory.Bruno, 1, 8f, 16f),
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 1, 10f, 15f), AbilityTestFactory.Slot(AbilityTestFactory.Bruno, 1, 12f, 16f));
            var inputs = TestMatchFactory.Inputs();

            for (int t = 0; t < AbilityWarmupTicks; t++) AbilityStep(sim, inputs, t);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = AbilityWarmupTicks; t < AbilityWarmupTicks + CombatTicks; t++) AbilityStep(sim, inputs, t);
            }, CombatTicks + " ticks with ults and tricks");

            int deaths = 0;
            for (int i = 0; i < Fighters; i++) deaths += sim.State.Stats[i].Deaths;
            Assert.Greater(deaths, 0, "the measured ticks must include defeats");
        }

        private const int AbilityWarmupTicks = 120;

        private static void AbilityStep(MatchSimulation sim, PlayerInput[] inputs, int t)
        {
            // Rotate attack / ult / trick presses (with releases in between) and keep the ults charged.
            InputButtons buttons;
            switch (t % 6)
            {
                case 0: buttons = InputButtons.Attack | InputButtons.AutoAim; break;
                case 2: buttons = InputButtons.Ult | InputButtons.AutoAim; break;
                case 4: buttons = InputButtons.Trick; break;
                default: buttons = InputButtons.None; break;
            }
            var move = new Vec2(t % 40 < 20 ? 1f : -1f, 0f);
            for (int i = 0; i < Fighters; i++)
            {
                inputs[i] = new PlayerInput(move, Vec2.Zero, buttons);
                if (t % 30 == 1) AbilityTestFactory.ChargeUlt(sim, i);
            }
            sim.Tick(inputs);
        }

        [Test]
        public void SnapshotCopy_DoesNotAllocate()
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(Fighters, 4));
            var snapshot = new MatchState();
            snapshot.CopyFrom(sim.State);
            snapshot.ContentEquals(sim.State);

            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    snapshot.CopyFrom(sim.State);
                    sim.State.CopyFrom(snapshot);
                    snapshot.ContentEquals(sim.State);
                }
            });
        }

        private static void Step(MatchSimulation sim, PlayerInput[] inputs, ref DeterministicRng rng, int t)
        {
            TestMatchFactory.FillRandomInputs(ref rng, inputs, Fighters);
            // Exercise charge spending and regeneration paths too.
            if (t % 20 == 0)
            {
                ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[t % Fighters]);
                sim.State.Fighters[(t + 1) % Fighters].Health -= 50;
            }
            sim.Tick(inputs);
        }
    }
}
