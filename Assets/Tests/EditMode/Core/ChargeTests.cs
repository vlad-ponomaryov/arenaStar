using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class ChargeTests
    {
        private const int Recharge = TestMatchFactory.RechargeTicks;

        [Test]
        public void StartsFull_SpendsOneEach_FailsWhenEmpty()
        {
            var sim = TestMatchFactory.CreateSingle();
            Assert.AreEqual(3, sim.State.Fighters[0].Charges);

            Assert.IsTrue(ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]));
            Assert.IsTrue(ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]));
            Assert.IsTrue(ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]));
            Assert.AreEqual(0, sim.State.Fighters[0].Charges);
            Assert.IsFalse(ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]));
            Assert.AreEqual(0, sim.State.Fighters[0].Charges);
        }

        [Test]
        public void RestoresOneByOne_AtRechargeInterval()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            for (int i = 0; i < 3; i++) ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]);

            TestMatchFactory.Run(sim, inputs, Recharge - 1);
            Assert.AreEqual(0, sim.State.Fighters[0].Charges);
            sim.Tick(inputs);
            Assert.AreEqual(1, sim.State.Fighters[0].Charges);
            Assert.AreEqual(1, sim.Events.CountOf(SimEventType.ChargeRestored));

            TestMatchFactory.Run(sim, inputs, Recharge);
            Assert.AreEqual(2, sim.State.Fighters[0].Charges);
            TestMatchFactory.Run(sim, inputs, Recharge);
            Assert.AreEqual(3, sim.State.Fighters[0].Charges);
        }

        [Test]
        public void NeverExceedsMax()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]);

            TestMatchFactory.Run(sim, inputs, Recharge * 10);

            Assert.AreEqual(3, sim.State.Fighters[0].Charges);
            Assert.AreEqual(0, sim.State.Fighters[0].ChargeProgressTicks);
        }

        [Test]
        public void SpendingDuringRecharge_KeepsProgress()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]);
            TestMatchFactory.Run(sim, inputs, 20);

            ChargeSystem.TryConsumeCharge(ref sim.State.Fighters[0]);
            Assert.AreEqual(1, sim.State.Fighters[0].Charges);
            TestMatchFactory.Run(sim, inputs, Recharge - 20);
            Assert.AreEqual(2, sim.State.Fighters[0].Charges, "first charge finished on its original schedule");
        }
    }
}
