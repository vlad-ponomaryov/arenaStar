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
    public sealed class TrickTests
    {
        /// <summary>Runs ticks without input until the next tick to simulate has index <paramref name="tick"/>.</summary>
        private static void RunUntilTick(MatchSimulation sim, Arena.Core.Input.PlayerInput[] inputs, int tick)
        {
            while (sim.State.Tick < tick) sim.Tick(inputs);
        }

        [Test]
        public void Trick_ThreeUsesPerMatch_With150TickCooldown()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 10f));
            var inputs = F.Inputs();
            ref var f = ref sim.State.Fighters[0];
            Assert.AreEqual(F.TrickUses, f.TrickUsesLeft);

            int t0 = sim.State.Tick;
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(1, sim.Events.CountOf(SimEventType.AbilityFired), "first use");
            Assert.AreEqual(2, f.TrickUsesLeft);

            RunUntilTick(sim, inputs, t0 + 100);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.AbilityFired), "on cooldown");
            Assert.AreEqual(2, f.TrickUsesLeft);

            Assert.IsFalse(TrickSystem.IsReady(sim.Defs, in f, t0 + F.TrickCooldownTicks - 1));
            Assert.IsTrue(TrickSystem.IsReady(sim.Defs, in f, t0 + F.TrickCooldownTicks));
            RunUntilTick(sim, inputs, t0 + F.TrickCooldownTicks);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(1, sim.Events.CountOf(SimEventType.AbilityFired), "ready exactly 150 ticks later");
            Assert.AreEqual(1, f.TrickUsesLeft);

            RunUntilTick(sim, inputs, t0 + 2 * F.TrickCooldownTicks);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(0, f.TrickUsesLeft, "third use");

            RunUntilTick(sim, inputs, t0 + 4 * F.TrickCooldownTicks);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.AbilityFired), "no fourth use");
        }

        [Test]
        public void Trick_UsesSurviveRespawn()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 1, 0, 100000);
            F.Run(sim, inputs, F.RespawnTicks + 1);
            Assert.IsTrue(sim.State.Fighters[0].Alive);
            Assert.AreEqual(F.TrickUses - 1, sim.State.Fighters[0].TrickUsesLeft, "3 uses per match, not per life");
        }

        [TestCase(4, false)]
        [TestCase(5, true)]
        public void Trick_OpensAtLevel5_WhenNotUnlockAll(int level, bool expected)
        {
            var sim = A.Create(false, A.Slot(A.Bruno, 0, 10f, 10f, level));
            var inputs = F.Inputs();
            Assert.AreEqual(expected, sim.State.Fighters[0].TrickUnlocked);
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(expected ? 1 : 0, sim.Events.CountOf(SimEventType.AbilityFired));
        }

        [Test]
        public void Trick_UnlockAll_OpensAtLevel1()
        {
            var sim = A.Create(true, A.Slot(A.Bruno, 0, 10f, 10f, 1));
            Assert.IsTrue(sim.State.Fighters[0].TrickUnlocked);
            Assert.IsTrue(sim.State.Fighters[0].TalentUnlocked);
        }

        [Test]
        public void PixRoll_Dashes3Cells_AlongMovement()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f));
            var inputs = F.Inputs();
            Vec2 start = sim.State.Fighters[0].Position;
            A.Press(sim, inputs, 0, InputButtons.Trick, default, Vec2.Right);
            Assert.IsTrue(sim.State.Fighters[0].IsDashing);

            F.Run(sim, inputs, A.PixRollTicks + 5);
            float walked = F.Speed * SimTime.Dt; // the movement of the press tick happens before the trick
            Assert.IsFalse(sim.State.Fighters[0].IsDashing);
            Assert.AreEqual(start.X + walked + A.PixRollDistance, sim.State.Fighters[0].Position.X, 1e-3f);
            Assert.AreEqual(start.Y, sim.State.Fighters[0].Position.Y, 1e-4f);
        }

        [Test]
        public void PixRoll_DurationFromSpeed_AndInputIgnoredMeanwhile()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f));
            var inputs = F.Inputs();
            sim.State.Fighters[0].Facing = Vec2.Up;
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(A.PixRollTicks - 1, sim.State.Fighters[0].DashTicksLeft, "3 cells at 15 cells/s = 6 ticks, first one done");

            inputs[0] = new PlayerInput(Vec2.Right, Vec2.Zero, InputButtons.None); // steering is ignored while dashing
            F.Run(sim, inputs, A.PixRollTicks - 1);
            Assert.AreEqual(10f, sim.State.Fighters[0].Position.X, 1e-4f);
            Assert.AreEqual(10f + A.PixRollDistance, sim.State.Fighters[0].Position.Y, 1e-3f, "no movement input: along the facing");
        }

        [Test]
        public void WhileDashing_AttackIsNotStarted()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 25f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.AbilityFired));
            Assert.AreEqual(3, sim.State.Fighters[0].Charges, "no charge spent");
        }

        [Test]
        public void AttackAndTrick_InTheSameTick_BothHappen()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 25f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Attack | InputButtons.Trick, Vec2.Up);
            Assert.AreEqual(2, sim.Events.CountOf(SimEventType.AbilityFired));
        }
    }
}
