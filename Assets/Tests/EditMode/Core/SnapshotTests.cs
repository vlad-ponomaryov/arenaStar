using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class SnapshotTests
    {
        private const int Fighters = 6;

        [Test]
        public void CopyFrom_ThenSameInputs_StaysEqual()
        {
            var defs = TestMatchFactory.CreateDefs();
            var a = new MatchSimulation(defs, TestMatchFactory.CreateSetup(Fighters, 5));
            var b = new MatchSimulation(defs, TestMatchFactory.CreateSetup(Fighters, 5));
            var inputs = TestMatchFactory.Inputs();
            var rng = DeterministicRng.FromSeed(11);

            for (int t = 0; t < 50; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rng, inputs, Fighters);
                a.Tick(inputs);
            }
            b.State.CopyFrom(a.State);
            Assert.IsTrue(a.State.ContentEquals(b.State));

            for (int t = 0; t < 200; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rng, inputs, Fighters);
                a.Tick(inputs);
                b.Tick(inputs);
            }
            Assert.IsTrue(a.State.ContentEquals(b.State));
            Assert.AreEqual(250, b.State.Tick);
        }

        [Test]
        public void Restore_AndResimulate_GivesSameResult()
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(Fighters, 9));
            var inputs = TestMatchFactory.Inputs();
            var rng = DeterministicRng.FromSeed(3);
            TestMatchFactory.Run(sim, inputs, 30);

            var snapshot = new MatchState();
            snapshot.CopyFrom(sim.State);
            DeterministicRng inputRngAtSnapshot = rng;

            for (int t = 0; t < 120; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rng, inputs, Fighters);
                sim.Tick(inputs);
            }
            var firstRun = new MatchState();
            firstRun.CopyFrom(sim.State);

            sim.State.CopyFrom(snapshot);
            rng = inputRngAtSnapshot;
            for (int t = 0; t < 120; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rng, inputs, Fighters);
                sim.Tick(inputs);
            }
            Assert.IsTrue(firstRun.ContentEquals(sim.State));
        }

        [Test]
        public void CopyFrom_IsDeep_AndContentEqualsDetectsChanges()
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(Fighters));
            var copy = new MatchState();
            copy.CopyFrom(sim.State);
            Assert.IsTrue(copy.ContentEquals(sim.State));

            copy.Fighters[Fighters - 1].Health -= 1;
            Assert.IsFalse(copy.ContentEquals(sim.State));
            Assert.AreEqual(sim.State.Fighters[Fighters - 1].MaxHealth, sim.State.Fighters[Fighters - 1].Health, "source untouched");

            copy.CopyFrom(sim.State);
            copy.Globals.Rng.NextULong();
            Assert.IsFalse(copy.ContentEquals(sim.State), "RNG state is part of the snapshot");

            copy.CopyFrom(sim.State);
            copy.Globals.Tick++;
            Assert.IsFalse(copy.ContentEquals(sim.State));
            Assert.IsFalse(copy.ContentEquals(null));
        }
    }
}
