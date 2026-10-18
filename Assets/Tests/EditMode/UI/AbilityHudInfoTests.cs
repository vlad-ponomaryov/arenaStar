using Arena.Core.Input;
using Arena.Tests.EditMode.Core;
using Arena.UI.Hud;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;

namespace Arena.Tests.EditMode.UI
{
    public sealed class AbilityHudInfoTests
    {
        [Test]
        public void UltStick_IsActiveOnlyWhenCharged()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f));
            AbilityHudInfo info = AbilityHudInfo.From(sim, 0);
            Assert.IsTrue(info.HasUlt);
            Assert.IsFalse(info.UltCharged);
            Assert.AreEqual(0f, info.UltCharge);

            sim.State.Fighters[0].UltCharge = A.PixUltCharge / 2;
            Assert.AreEqual(0.5f, AbilityHudInfo.From(sim, 0).UltCharge, 1e-6f);

            A.ChargeUlt(sim, 0);
            info = AbilityHudInfo.From(sim, 0);
            Assert.IsTrue(info.UltCharged);
            Assert.AreEqual(1f, info.UltCharge, 1e-6f);
        }

        [Test]
        public void TrickDots_ShowUsesLeft_AndReadiness()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 10f));
            var inputs = TestMatchFactory.Inputs();
            AbilityHudInfo info = AbilityHudInfo.From(sim, 0);
            Assert.IsTrue(info.HasTrick);
            Assert.AreEqual(TestMatchFactory.TrickUses, info.TrickMaxUses);
            Assert.AreEqual(TestMatchFactory.TrickUses, info.TrickUsesLeft);
            Assert.IsTrue(info.TrickReady);

            A.Press(sim, inputs, 0, InputButtons.Trick);
            info = AbilityHudInfo.From(sim, 0);
            Assert.AreEqual(TestMatchFactory.TrickUses - 1, info.TrickUsesLeft);
            Assert.IsFalse(info.TrickReady, "on cooldown");
        }

        [Test]
        public void FighterWithoutAbilities_ShowsNothing()
        {
            var sim = A.Create(A.Slot(A.Dummy, 0, 10f, 10f));
            AbilityHudInfo info = AbilityHudInfo.From(sim, 0);
            Assert.IsFalse(info.HasUlt);
            Assert.IsFalse(info.HasTrick);
            Assert.AreEqual(0, info.TrickMaxUses);
            Assert.IsFalse(info.TrickReady);
        }
    }
}
