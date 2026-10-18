using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class StatusTests
    {
        [Test]
        public void HardHat_Reduces40Percent_For2Seconds()
        {
            var sim = A.Create(A.Slot(A.Bruno, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f));
            var inputs = F.Inputs();
            int t0 = sim.State.Tick;
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(0.6f, sim.State.Fighters[0].DamageTakenMul, 1e-6f);
            Assert.AreEqual(720, DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 1, 0, 1200), "−40 %");

            while (sim.State.Tick <= t0 + A.HelmetTicks) sim.Tick(inputs);
            Assert.AreEqual(0.6f, sim.State.Fighters[0].DamageTakenMul, 1e-6f, "active through the 60th tick after use");
            sim.Tick(inputs);
            Assert.AreEqual(1f, sim.State.Fighters[0].DamageTakenMul);
            Assert.AreEqual(1200, DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 1, 0, 1200));
        }

        [Test]
        public void Slow_ReducesSpeed_StrongestOfSeveralApplies()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f), A.Slot(A.Dummy, 1, 12f, 20f));
            var inputs = F.Inputs();
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Slow, 30, 100);
            StatusSystem.Apply(sim.State, sim.Events, 0, 2, 1, EffectKind.Slow, 50, 100);
            Assert.AreEqual(0.5f, sim.State.Fighters[0].SpeedMul, 1e-6f);

            inputs[0] = new PlayerInput(Vec2.Right, Vec2.Zero, InputButtons.None);
            float x = sim.State.Fighters[0].Position.X;
            F.Run(sim, inputs, 30);
            Assert.AreEqual(x + F.Speed * 0.5f, sim.State.Fighters[0].Position.X, 1e-3f);
        }

        [Test]
        public void SameSourceAndKind_RefreshesInsteadOfStacking()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f));
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Slow, 30, 10);
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Slow, 30, 40);
            Assert.AreEqual(1, StatusSystem.Count(sim.State, 0, EffectKind.Slow));
            Assert.AreEqual(sim.State.Tick + 1 + 40, FindExpire(sim, 0, EffectKind.Slow), "the later expiry wins");
        }

        [Test]
        public void Statuses_AreClearedOnDefeat()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f));
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Stun, 0, 100);
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Mark, 0, 100);
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 1, 0, 100000);
            Assert.AreEqual(0, StatusSystem.Count(sim.State, 0, EffectKind.Stun));
            Assert.AreEqual(0, StatusSystem.Count(sim.State, 0, EffectKind.Mark));
            Assert.IsFalse(sim.State.Fighters[0].Stunned);
            Assert.AreEqual(0u, sim.State.Fighters[0].MarkedByTeamMask);
        }

        [Test]
        public void Overflow_IsCounted_WithoutException()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f));
            for (int i = 0; i < SimLimits.MaxStatusEffects; i++)
                Assert.IsTrue(StatusSystem.Apply(sim.State, sim.Events, 0, 100 + i, 1, EffectKind.Mark, 0, 50));
            Assert.IsFalse(StatusSystem.Apply(sim.State, sim.Events, 0, 99, 1, EffectKind.Mark, 0, 50));
            Assert.AreEqual(1, sim.State.Globals.DroppedStatuses);
            Assert.DoesNotThrow(() => sim.Tick(F.Inputs()));
        }

        [Test]
        public void Mark_SetsTheBitOfTheSourceTeam()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f));
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Mark, 0, 20);
            Assert.AreEqual(1u << 1, sim.State.Fighters[0].MarkedByTeamMask);
        }

        [Test]
        public void Invisibility_EndsWhenTheFighterAttacks()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 25f));
            var inputs = F.Inputs();
            StatusSystem.Apply(sim.State, sim.Events, 0, 0, 0, EffectKind.Invisibility, 0, 100);
            sim.Tick(inputs);
            Assert.IsTrue(sim.State.Fighters[0].Invisible);
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            Assert.IsFalse(sim.State.Fighters[0].Invisible);
            Assert.AreEqual(0, StatusSystem.Count(sim.State, 0, EffectKind.Invisibility));
        }

        [Test]
        public void PixUltKnockback_PushesAlongTheFlight()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 12f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            F.Run(sim, inputs, 15);
            Assert.Greater(sim.State.Fighters[1].Position.Y, 12f + 0.4f, "pushed away along +Y");
            Assert.Less(sim.State.Fighters[1].Position.Y, 12f + 2f, "a light push");
        }

        private static int FindExpire(MatchSimulation sim, int target, EffectKind kind)
        {
            foreach (var s in sim.State.Statuses)
                if (s.Active && s.Target == target && s.Kind == kind) return s.ExpireTick;
            return -1;
        }
    }
}
