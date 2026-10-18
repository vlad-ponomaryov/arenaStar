using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class DashStunTests
    {
        [Test]
        public void BrunoRam_Dashes6Cells_Deals1000_Knocks2Cells_Stuns15Ticks()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 5f), A.Slot(A.Dummy, 1, 10f, 8f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);

            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            Assert.IsTrue(sim.State.Fighters[0].IsDashing);

            int hitTick = sim.Events.CountOf(SimEventType.Damaged) > 0 ? sim.State.Tick - 1 : -1;
            while (hitTick < 0 && sim.State.Tick < 30)
            {
                sim.Tick(inputs);
                if (sim.Events.CountOf(SimEventType.Damaged) > 0) hitTick = sim.State.Tick - 1;
            }
            Assert.GreaterOrEqual(hitTick, 0, "the ram reaches the dummy 3 cells ahead");
            ref var victim = ref sim.State.Fighters[1];
            Assert.AreEqual(A.DummyHealth - A.RamDamage, victim.Health);
            Assert.IsTrue(victim.Stunned);
            Vec2 atHit = victim.Position;

            // From now on the victim keeps trying to walk right: it must not move by itself while stunned.
            inputs[1] = new PlayerInput(Vec2.Right, Vec2.Zero, InputButtons.None);

            F.Run(sim, inputs, A.RamPushTicks);
            Assert.AreEqual(atHit.Y + A.RamPush, victim.Position.Y, 1e-3f, "knockback 2 cells along the ram");
            Assert.AreEqual(atHit.X, victim.Position.X, 1e-4f, "no own movement while stunned");

            // Stunned during ticks hit+1 … hit+15; walks again from hit+16.
            while (sim.State.Tick <= hitTick + A.RamStunTicks) sim.Tick(inputs);
            Assert.IsTrue(victim.Stunned, "still stunned in the 15th tick after the hit");
            Assert.AreEqual(atHit.X, victim.Position.X, 1e-4f);
            sim.Tick(inputs);
            Assert.IsFalse(victim.Stunned);
            Assert.Greater(victim.Position.X, atHit.X + 1e-3f, "moves again after 15 ticks");

            // The ram itself: 6 cells in 12 ticks, then over.
            Assert.IsFalse(sim.State.Fighters[0].IsDashing);
            Assert.AreEqual(5f + A.RamDistance, sim.State.Fighters[0].Position.Y, 1e-3f);
            Assert.AreEqual(0u, sim.State.Fighters[0].DashHitMask, "dash state cleared");
        }

        [Test]
        public void BrunoRam_HitsEachTargetOnce_AndNotAllies()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 5f), A.Slot(A.Dummy, 1, 10f, 7f), A.Slot(A.Dummy, 0, 10f, 9f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            int damaged = sim.Events.CountOf(SimEventType.Damaged);
            for (int t = 0; t < A.RamTicks + 2; t++)
            {
                sim.Tick(inputs);
                damaged += sim.Events.CountOf(SimEventType.Damaged);
            }
            Assert.AreEqual(1, damaged, "one hit on the enemy, none on the ally");
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[2].Health);
            Assert.AreEqual(0, StatusSystem.Count(sim.State, 2, EffectKind.Stun));
        }

        [Test]
        public void Ram_AutoAim_TargetsNearestEnemyWithinRange()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 10f), A.Slot(A.Dummy, 1, 14f, 10f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult | InputButtons.AutoAim);
            Assert.AreEqual(1f, sim.State.Fighters[0].DashVelocity.Normalized.X, 1e-4f, "towards the enemy on the right");
        }

        [Test]
        public void StunnedFighter_StandsAndDoesNotAttack()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 14f));
            var inputs = F.Inputs();
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Stun, 0, 10);
            Vec2 start = sim.State.Fighters[0].Position;

            inputs[0] = new PlayerInput(Vec2.Right, Vec2.Up, InputButtons.Attack);
            sim.Tick(inputs);
            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.AbilityFired));
            Assert.AreEqual(3, sim.State.Fighters[0].Charges);
            Assert.AreEqual(start, sim.State.Fighters[0].Position);
        }

        [Test]
        public void Stun_CancelsBurstInProgress()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 25f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            Assert.AreEqual(F.BurstCount - 1, sim.State.Fighters[0].BurstShotsLeft);
            var ctx = new Arena.Core.Sim.Effects.EffectContext(sim.Defs.Fighters[A.Bruno].Ult, -1, 1, 1, Vec2.Zero, Vec2.Zero);
            Arena.Core.Sim.Effects.StunEffect.Apply(sim.State, sim.Events, in ctx, sim.Defs.Fighters[A.Bruno].Ult.Effects[2], 0);
            Assert.AreEqual(0, sim.State.Fighters[0].BurstShotsLeft);
        }
    }
}
