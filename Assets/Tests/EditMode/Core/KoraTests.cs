using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;
using S = Arena.Tests.EditMode.Core.SupportFighterTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Kora (S2.11): bouncing cup, dome (projectile block + heal over time), brew, regen aura.</summary>
    public sealed class KoraTests
    {
        private const int Wounded = 40000;

        [Test]
        public void Cup_BouncesUpTo3Targets_HealsAlly_SkipsAlreadyHit()
        {
            var sim = S.Create(
                S.Slot(S.Kora, 0, 5.5f, 5.5f),
                S.Slot(S.Dummy, 1, 8.5f, 5.5f),    // 1st: enemy on the line
                S.Slot(S.Dummy, 0, 8.5f, 7.5f),    // 2nd: ally 2 cells from it (closer than the next enemy)
                S.Slot(S.Dummy, 1, 10.5f, 5.5f),   // 3rd: enemy 2.83 from the ally (the 1st, at 2, is skipped)
                S.Slot(S.Dummy, 1, 10.5f, 3.5f));  // would be the 4th
            S.Wound(sim, 2, Wounded);
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            int hits = sim.Events.CountOf(SimEventType.ProjectileHit);
            for (int t = 0; t < 60; t++)
            {
                sim.Tick(inputs);
                hits += sim.Events.CountOf(SimEventType.ProjectileHit);
            }

            Assert.AreEqual(3, hits, "three targets in all");
            Assert.AreEqual(A.DummyHealth - S.CupDamage, sim.State.Fighters[1].Health, "hit once");
            Assert.AreEqual(Wounded + S.CupHeal, sim.State.Fighters[2].Health, "ally healed");
            Assert.AreEqual(A.DummyHealth - S.CupDamage, sim.State.Fighters[3].Health);
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[4].Health, "no fourth target");
            Assert.AreEqual(S.KoraHealth, sim.State.Fighters[0].Health, "never bounces back to Kora");
        }

        [Test]
        public void Cup_WithoutTargetInBounceRange_Stops()
        {
            var sim = S.Create(S.Slot(S.Kora, 0, 5.5f, 5.5f), S.Slot(S.Dummy, 1, 8.5f, 5.5f), S.Slot(S.Dummy, 1, 8.5f, 9.6f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            F.Run(sim, inputs, 60);
            Assert.AreEqual(A.DummyHealth - S.CupDamage, sim.State.Fighters[1].Health);
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[2].Health, "4.1 cells away: out of the bounce range");
        }

        [Test]
        public void Dome_BlocksEnemyProjectileFromOutside()
        {
            var sim = S.Create(
                S.Slot(S.Kora, 0, 10.5f, 15.5f),
                S.Slot(S.Dummy, 0, 10.5f, 16.5f),       // ally inside the dome
                S.Slot(S.Shooter, 1, 10.5f, 22.5f));    // enemy shooter outside, 6 cells above the ally
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult);
            CombatTestUtil.PressAttack(sim, inputs, 2, new Vec2(0f, -1f));
            int ended = 0;
            float endY = 0f;
            for (int t = 0; t < 30; t++)
            {
                sim.Tick(inputs);
                for (int e = 0; e < sim.Events.Count; e++)
                {
                    if (sim.Events[e].Type != SimEventType.ProjectileEnded) continue;
                    ended++;
                    endY = sim.Events[e].Position.Y;
                }
            }
            Assert.AreEqual(A.DummyHealth, sim.State.Fighters[1].Health, "the dome stops the drops");
            Assert.AreEqual(F.BurstCount, ended);
            Assert.AreEqual(15.5f + S.DomeRadius, endY, 0.01f, "stopped at the edge");
        }

        [Test]
        public void Dome_LetsProjectilesOutFromInside()
        {
            var sim = S.Create(
                S.Slot(S.Kora, 0, 10.5f, 15.5f),
                S.Slot(S.Shooter, 0, 10.5f, 15f),     // own shooter inside
                S.Slot(S.Dummy, 1, 10.5f, 20.5f),     // enemy outside
                S.Slot(S.Shooter, 1, 11.5f, 16f));    // enemy shooter that walked inside
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult);
            CombatTestUtil.PressAttack(sim, inputs, 1, Vec2.Up);
            F.Run(sim, inputs, 30);
            Assert.AreEqual(A.DummyHealth - F.BurstCount * F.DropDamage, sim.State.Fighters[2].Health, "own shots fly out");

            int before = sim.State.Fighters[0].Health;
            CombatTestUtil.PressAttack(sim, inputs, 3, new Vec2(-1f, 0f));
            F.Run(sim, inputs, 10);
            Assert.Less(sim.State.Fighters[0].Health, before, "an enemy inside is not blocked");
        }

        [Test]
        public void Dome_Heals1000OverThreeSeconds_InsideOnly_ThenDisappears()
        {
            var sim = S.Create(S.Slot(S.Kora, 0, 10.5f, 15.5f), S.Slot(S.Dummy, 0, 11.5f, 16.5f), S.Slot(S.Dummy, 0, 15.5f, 15.5f));
            S.Wound(sim, 0, 2000);
            S.Wound(sim, 1, Wounded);
            S.Wound(sim, 2, Wounded);
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult);
            F.Run(sim, inputs, S.DomeTicks / 2);
            Assert.Less(sim.State.Fighters[1].Health, Wounded + S.DomeHeal, "spread over the duration");
            Assert.Greater(sim.State.Fighters[1].Health, Wounded);

            F.Run(sim, inputs, S.DomeTicks / 2 + 5);
            Assert.AreEqual(Wounded + S.DomeHeal, sim.State.Fighters[1].Health, "exactly 1000 in all");
            Assert.AreEqual(2000 + S.DomeHeal, sim.State.Fighters[0].Health, "Kora inside too");
            Assert.AreEqual(Wounded, sim.State.Fighters[2].Health, "outside");
            for (int i = 0; i < sim.State.Areas.Length; i++) Assert.IsFalse(sim.State.Areas[i].Active, "gone after 3 s");
        }

        [Test]
        public void Brew_HealsSelfAndAlliesWithin3_NotEnemies()
        {
            float reach = S.BrewRadius + A.DummyRadius;
            var sim = S.Create(
                S.Slot(S.Kora, 0, 10.5f, 15.5f),
                S.Slot(S.Dummy, 0, 10.5f + reach - 0.05f, 15.5f),
                S.Slot(S.Dummy, 0, 10.5f, 15.5f + reach + 0.05f),
                S.Slot(S.Dummy, 1, 9.5f, 15.5f));
            S.Wound(sim, 0, 2000);
            for (int i = 1; i <= 3; i++) S.Wound(sim, i, Wounded);
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.AreEqual(2000 + S.BrewHeal, sim.State.Fighters[0].Health);
            Assert.AreEqual(Wounded + S.BrewHeal, sim.State.Fighters[1].Health, "inside");
            Assert.AreEqual(Wounded, sim.State.Fighters[2].Health, "outside");
            Assert.AreEqual(Wounded, sim.State.Fighters[3].Health, "enemy");
        }

        [Test]
        public void Aura_AllyNearKoraRegeneratesAfter2s_FarAllyAndKoraAfter3s()
        {
            var sim = S.Create(S.Slot(S.Kora, 0, 10.5f, 15.5f), S.Slot(S.Dummy, 0, 12.5f, 15.5f), S.Slot(S.Dummy, 0, 16.5f, 15.5f));
            var inputs = F.Inputs();
            for (int i = 0; i < 3; i++) DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, -1, i, 1000);
            int near = sim.State.Fighters[1].Health, far = sim.State.Fighters[2].Health, kora = sim.State.Fighters[0].Health;

            F.Run(sim, inputs, S.AuraRegenTicks);
            Assert.AreEqual(near, sim.State.Fighters[1].Health, "not before 2 s");
            sim.Tick(inputs);
            Assert.Greater(sim.State.Fighters[1].Health, near, "near ally: after 2 s");
            Assert.AreEqual(far, sim.State.Fighters[2].Health, "far ally: still waiting");
            Assert.AreEqual(kora, sim.State.Fighters[0].Health, "the aura is for allies");

            F.Run(sim, inputs, F.RegenDelayTicks - S.AuraRegenTicks);
            Assert.Greater(sim.State.Fighters[2].Health, far, "far ally: after 3 s");
            Assert.Greater(sim.State.Fighters[0].Health, kora);
        }
    }
}
