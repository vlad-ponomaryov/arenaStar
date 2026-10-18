using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class TalentTests
    {
        private static float WalkOneSecond(MatchSimulation sim, int slot)
        {
            var inputs = F.Inputs();
            inputs[slot] = new PlayerInput(Vec2.Right, Vec2.Zero, InputButtons.None);
            float x = sim.State.Fighters[slot].Position.X;
            F.Run(sim, inputs, SimLimits.TickRate);
            return sim.State.Fighters[slot].Position.X - x;
        }

        [Test]
        public void PixTalent_SpeedPlus10_OnlyWhileUltIsCharged()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 3f, 10f));
            Assert.AreEqual(F.Speed, WalkOneSecond(sim, 0), 1e-3f, "ult not charged: normal speed");

            A.ChargeUlt(sim, 0);
            Assert.IsTrue(TalentSystem.IsActive(sim.State, sim.Defs, 0));
            Assert.AreEqual(F.Speed * 1.1f, WalkOneSecond(sim, 0), 1e-3f, "charged: +10 %");
        }

        [TestCase(7, false)]
        [TestCase(8, true)]
        public void Talent_OpensAtLevel8_WhenNotUnlockAll(int level, bool expected)
        {
            var sim = A.Create(false, A.Slot(A.Pix, 0, 3f, 10f, level));
            A.ChargeUlt(sim, 0);
            Assert.AreEqual(expected, sim.State.Fighters[0].TalentUnlocked);
            Assert.AreEqual(expected ? F.Speed * 1.1f : F.Speed, WalkOneSecond(sim, 0), 1e-3f);
        }

        private static int BrunoSwingDamage(int health)
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 11.5f));
            var inputs = F.Inputs();
            sim.State.Fighters[0].Health = health;
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            Assert.AreEqual(1, sim.Events.CountOf(SimEventType.Damaged));
            return A.DummyHealth - sim.State.Fighters[1].Health;
        }

        [Test]
        public void BrunoTalent_DamagePlus15_OnlyBelow40PercentHealth()
        {
            Assert.AreEqual(F.ConeDamage, BrunoSwingDamage(A.BrunoHealth), "full health");
            Assert.AreEqual(F.ConeDamage, BrunoSwingDamage(A.BrunoHealth * 40 / 100), "exactly 40 % is not below");
            Assert.AreEqual(1380, BrunoSwingDamage(A.BrunoHealth * 40 / 100 - 1), "1200 × 1.15");
        }
    }
}
