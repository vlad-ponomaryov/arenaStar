using Arena.Core.Input;
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
