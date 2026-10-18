using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class RegenTests
    {
        private const int Delay = TestMatchFactory.RegenDelayTicks;
        private const int Damaged = 1000;
        private static readonly float PerSecond = TestMatchFactory.MaxHealth * TestMatchFactory.RegenFraction; // 408

        private static MatchSimulation CreateDamaged()
        {
            var sim = TestMatchFactory.CreateSingle();
            sim.State.Fighters[0].Health = Damaged;
            RegenSystem.MarkInCombat(ref sim.State.Fighters[0], sim.State.Tick);
            return sim;
        }

        [Test]
        public void NoRegen_BeforeDelay()
        {
            var sim = CreateDamaged();
            TestMatchFactory.Run(sim, TestMatchFactory.Inputs(), Delay);
            Assert.AreEqual(Damaged, sim.State.Fighters[0].Health);
        }

        [Test]
        public void AfterDelay_RegensFractionPerSecond()
        {
            var sim = CreateDamaged();
            var inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, Delay);

            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate);
            Assert.AreEqual(Damaged + PerSecond, sim.State.Fighters[0].Health, PerSecond * 0.01f);

            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate * 2);
            Assert.AreEqual(Damaged + PerSecond * 3f, sim.State.Fighters[0].Health, PerSecond * 3f * 0.01f);
        }

        [Test]
        public void Damage_ResetsDelay()
        {
            var sim = CreateDamaged();
            var inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, Delay + SimLimits.TickRate);
            Assert.Greater(sim.State.Fighters[0].Health, Damaged);

            sim.State.Fighters[0].Health = Damaged;
            RegenSystem.MarkInCombat(ref sim.State.Fighters[0], sim.State.Tick);
            TestMatchFactory.Run(sim, inputs, Delay);
            Assert.AreEqual(Damaged, sim.State.Fighters[0].Health);
            Assert.AreEqual(0f, sim.State.Fighters[0].RegenRemainder);

            sim.Tick(inputs);
            Assert.Greater(sim.State.Fighters[0].Health, Damaged);
        }

        [Test]
        public void NeverExceedsMax()
        {
            var sim = TestMatchFactory.CreateSingle();
            sim.State.Fighters[0].Health = TestMatchFactory.MaxHealth - 5;
            RegenSystem.MarkInCombat(ref sim.State.Fighters[0], sim.State.Tick);
            TestMatchFactory.Run(sim, TestMatchFactory.Inputs(), Delay + SimLimits.TickRate * 5);
            Assert.AreEqual(TestMatchFactory.MaxHealth, sim.State.Fighters[0].Health);
            Assert.AreEqual(0f, sim.State.Fighters[0].RegenRemainder);
        }
    }
}
