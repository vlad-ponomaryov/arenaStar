using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    public sealed class UltTests
    {
        private static MatchSimulation PixVsDummy(float dummyY = 13f, int level = 1) =>
            A.Create(A.Slot(A.Pix, 0, 10f, 10f, level), A.Slot(A.Dummy, 1, 10f, dummyY));

        private static int ActiveProjectiles(MatchSimulation sim)
        {
            int n = 0;
            for (int i = 0; i < sim.State.Projectiles.Length; i++)
                if (sim.State.Projectiles[i].Active) n++;
            return n;
        }

        [Test]
        public void UltCharge_GrowsFromAttackDamage()
        {
            var sim = PixVsDummy();
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            F.Run(sim, inputs, 30);

            Assert.AreEqual(F.DropDamage * F.BurstCount, sim.State.Stats[0].DamageDealt, "all 4 drops hit");
            Assert.AreEqual(F.DropDamage * F.BurstCount, sim.State.Fighters[0].UltCharge, "ult charge = attack damage dealt");
        }

        [Test]
        public void Ult_NotCharged_CannotBeUsed()
        {
            var sim = PixVsDummy();
            var inputs = F.Inputs();
            sim.State.Fighters[0].UltCharge = A.PixUltCharge - 1;
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);

            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.AbilityFired));
            Assert.AreEqual(0, ActiveProjectiles(sim));
            Assert.AreEqual(A.PixUltCharge - 1, sim.State.Fighters[0].UltCharge, "charge is kept");
        }

        [Test]
        public void Ult_Charged_FiresFanOfTen_WithSpread_AndResetsCharge()
        {
            var sim = PixVsDummy(25f);
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);

            Assert.AreEqual(0, sim.State.Fighters[0].UltCharge, "charge is spent");
            Assert.AreEqual(1, sim.Events.CountOf(SimEventType.AbilityFired));
            Assert.AreEqual(A.PixUltDrops, ActiveProjectiles(sim));

            float min = float.MaxValue, max = float.MinValue;
            int ultRef = MatchDefs.MakeAbilityRef(A.Pix, AbilitySlot.Ult);
            var angles = new System.Collections.Generic.List<float>();
            foreach (ProjectileState p in sim.State.Projectiles)
            {
                if (!p.Active) continue;
                Assert.AreEqual(ultRef, p.AbilityRef);
                float a = MathUtil.DirToAngle(p.Dir);
                angles.Add(a);
                min = MathUtil.Min(min, a);
                max = MathUtil.Max(max, a);
            }
            Assert.AreEqual(A.PixUltSpread, max - min, 1e-3f, "fan spread between the outer drops");
            Assert.AreEqual(90f, (min + max) * 0.5f, 1e-3f, "fan centred on the aim");
            angles.Sort();
            for (int i = 1; i < angles.Count; i++)
                Assert.AreEqual(A.PixUltSpread / (A.PixUltDrops - 1), angles[i] - angles[i - 1], 1e-3f, "even spacing");
        }

        [Test]
        public void UltDamage_DoesNotChargeUlt_AndIsLevelScaled()
        {
            var sim = PixVsDummy(13f, 3);
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            Assert.AreEqual(8512, UltSystem.Required(sim.Defs, in sim.State.Fighters[0]), "requirement scales with level: 7600 × 1.12");

            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            int hits = 0;
            for (int t = 0; t < 20; t++)
            {
                for (int e = 0; e < sim.Events.Count; e++)
                {
                    if (sim.Events[e].Type != SimEventType.Damaged) continue;
                    Assert.AreEqual(448, sim.Events[e].Value, "400 × (1 + 0.06 × 2)");
                    hits++;
                }
                sim.Tick(inputs);
            }
            Assert.Greater(hits, 0, "the fan hits the dummy 3 cells ahead");
            Assert.AreEqual(0, sim.State.Fighters[0].UltCharge, "ult damage does not charge the ult");
        }

        [Test]
        public void UltReady_EventOnce_AndChargeIsCapped()
        {
            var sim = PixVsDummy();
            var inputs = F.Inputs();
            sim.State.Fighters[0].UltCharge = A.PixUltCharge - 100;
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            int ready = 0;
            for (int t = 0; t < 30; t++)
            {
                ready += sim.Events.CountOf(SimEventType.UltReady);
                sim.Tick(inputs);
            }
            Assert.AreEqual(1, ready);
            Assert.AreEqual(A.PixUltCharge, sim.State.Fighters[0].UltCharge);
            Assert.IsTrue(UltSystem.IsCharged(sim.Defs, in sim.State.Fighters[0]));
        }

        [Test]
        public void Death_LosesHalfTheCharge_RespawnKeepsTheRest()
        {
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 20f));
            var inputs = F.Inputs();
            sim.State.Fighters[0].UltCharge = 3000;
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 1, 0, 100000);
            Assert.IsFalse(sim.State.Fighters[0].Alive);
            Assert.AreEqual(1500, sim.State.Fighters[0].UltCharge);

            F.Run(sim, inputs, F.RespawnTicks + 1);
            Assert.IsTrue(sim.State.Fighters[0].Alive);
            Assert.AreEqual(1500, sim.State.Fighters[0].UltCharge, "the remaining half survives the respawn");
        }

        [Test]
        public void Ult_WhileStunned_IsNotUsed()
        {
            var sim = PixVsDummy();
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 1, EffectKind.Stun, 0, 10);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            Assert.AreEqual(0, ActiveProjectiles(sim));
            Assert.IsTrue(UltSystem.IsCharged(sim.Defs, in sim.State.Fighters[0]), "the charge is not spent");
        }
    }
}
