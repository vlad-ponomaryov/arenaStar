using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class DeterminismTests
    {
        [Test]
        public void TwoSimulations_SameSeedAndInputs_IdenticalAfter900Ticks()
        {
            const int fighters = 6;
            const int seed = 12345;
            var a = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(fighters, seed));
            var b = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(fighters, seed));
            var inputsA = TestMatchFactory.Inputs();
            var inputsB = TestMatchFactory.Inputs();
            var rngA = DeterministicRng.FromSeed(777);
            var rngB = DeterministicRng.FromSeed(777);

            for (int t = 0; t < 900; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rngA, inputsA, fighters);
                TestMatchFactory.FillRandomInputs(ref rngB, inputsB, fighters);
                a.Tick(inputsA);
                b.Tick(inputsB);
            }

            Assert.AreEqual(900, a.State.Tick);
            Assert.IsTrue(a.State.ContentEquals(b.State));
            Assert.IsFalse(a.State.Fighters[0].Position == a.Setup.Slots[0].SpawnPosition, "fighters actually moved");
        }

        [Test]
        public void WithAbilities_SnapshotRestoreAndReplay_GivesIdenticalState()
        {
            const int fighters = 6;
            var sim = AbilityTestFactory.Create(
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 0, 8f, 12f), AbilityTestFactory.Slot(AbilityTestFactory.Bruno, 0, 10f, 13f),
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 0, 12f, 12f), AbilityTestFactory.Slot(AbilityTestFactory.Bruno, 1, 8f, 16f),
                AbilityTestFactory.Slot(AbilityTestFactory.Pix, 1, 10f, 15f), AbilityTestFactory.Slot(AbilityTestFactory.Bruno, 1, 12f, 16f));
            var inputs = TestMatchFactory.Inputs();
            var rng = DeterministicRng.FromSeed(99);
            for (int t = 0; t < 200; t++) RandomStep(sim, inputs, ref rng, fighters, t);

            var snapshot = new MatchState();
            snapshot.CopyFrom(sim.State);
            var rngAtSnapshot = rng;
            for (int t = 200; t < 500; t++) RandomStep(sim, inputs, ref rng, fighters, t);
            var first = new MatchState();
            first.CopyFrom(sim.State);

            sim.State.CopyFrom(snapshot);
            rng = rngAtSnapshot;
            for (int t = 200; t < 500; t++) RandomStep(sim, inputs, ref rng, fighters, t);
            Assert.IsTrue(first.ContentEquals(sim.State), "replay from a snapshot (statuses, dashes, ult charge) is identical");
        }

        private static void RandomStep(MatchSimulation sim, Arena.Core.Input.PlayerInput[] inputs, ref DeterministicRng rng, int fighters, int t)
        {
            TestMatchFactory.FillRandomInputs(ref rng, inputs, fighters);
            if (t % 25 == 0)
                for (int i = 0; i < fighters; i++) AbilityTestFactory.ChargeUlt(sim, i);
            sim.Tick(inputs);
        }

        [Test]
        public void Reset_RestoresInitialState()
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(4, 8));
            var initial = new MatchState();
            initial.CopyFrom(sim.State);
            var inputs = TestMatchFactory.Inputs();
            var rng = DeterministicRng.FromSeed(1);
            for (int t = 0; t < 100; t++)
            {
                TestMatchFactory.FillRandomInputs(ref rng, inputs, 4);
                sim.Tick(inputs);
            }
            Assert.IsFalse(initial.ContentEquals(sim.State));

            sim.Reset();
            Assert.IsTrue(initial.ContentEquals(sim.State));
        }
    }
}
