using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;
using N = Arena.Tests.EditMode.Core.ScoutFighterTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Nova (S2.12): pierce with falloff, mark, team reveal, boosted shots, push-back, marked-target talent — data + shared components.</summary>
    public sealed class NovaTests
    {
        private const float Tolerance = 0.05f;

        // Bush at column 8, row 2 (rows top first; 16 × 5).
        private static readonly string[] BushRow =
        {
            "................",
            "................",
            "........\".......",
            "................",
            "................"
        };

        // Wall column at x = 5 (rows 0..4).
        private static readonly string[] WallColumn =
        {
            ".....#..........",
            ".....#..........",
            ".....#..........",
            ".....#..........",
            ".....#.........."
        };

        /// <summary>Further ticks during which <paramref name="slot"/> stays visible to <paramref name="team"/>.</summary>
        private static int VisibleTicksAfter(MatchSimulation sim, PlayerInput[] inputs, int slot, int team)
        {
            int n = 0;
            for (int k = 0; k < 400; k++)
            {
                sim.Tick(inputs);
                if (!sim.State.Fighters[slot].IsVisibleTo(team)) break;
                n++;
            }
            return n;
        }

        [Test]
        public void Cone_PiercesFirst_SecondTakesHalf_ThirdUntouched()
        {
            var sim = N.Create(false, N.CreateDefs(),
                N.Slot(N.Nova, 0, 2.5f, 5.5f),
                N.Slot(N.Dummy, 1, 5.5f, 5.5f),
                N.Slot(N.Dummy, 1, 7.5f, 5.5f),
                N.Slot(N.Dummy, 1, 9.5f, 5.5f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            F.Run(sim, inputs, 30);

            Assert.AreEqual(N.ConeDamage, N.Lost(sim, 1), "first target: full damage");
            Assert.AreEqual(N.ConeDamage * (100 - N.PierceFalloff) / 100, N.Lost(sim, 2), "second target: 50 %");
            Assert.AreEqual(0, N.Lost(sim, 3), "pierces only the first");
        }

        [Test]
        public void Mark_KeepsTargetVisibleInBush_For4Seconds()
        {
            var sim = N.Create(false, N.CreateDefsOnMap(BushRow), N.Slot(N.Nova, 0, 2.5f, 2.5f), N.Slot(N.Dummy, 1, 8.5f, 2.5f));
            var inputs = F.Inputs();
            F.Run(sim, inputs, 1);
            Assert.IsFalse(sim.State.Fighters[1].IsVisibleTo(0), "hidden in the bush, 6 cells away");

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            int hit = CombatTestUtil.RunUntil(sim, inputs, SimEventType.ProjectileHit, 30);
            Assert.GreaterOrEqual(hit, 0);
            Assert.AreEqual(1, StatusSystem.Count(sim.State, 1, EffectKind.Mark));
            Assert.IsTrue(sim.State.Fighters[1].IsVisibleTo(0), "marked: visible to Nova's team");
            Assert.IsFalse(sim.State.Fighters[1].IsVisibleTo(2), "only to the marking team");

            Assert.AreEqual(N.MarkTicks, VisibleTicksAfter(sim, inputs, 1, 0), "visible for exactly 4 s after the hit tick");
        }

        [Test]
        public void Talent_PlusTwentyPercentOnMarkedTargets_OnlyWhenUnlocked()
        {
            foreach (bool unlocked in new[] { true, false })
            {
                var sim = N.Create(unlocked, N.CreateDefs(), N.Slot(N.Nova, 0, 2.5f, 5.5f), N.Slot(N.Dummy, 1, 8.5f, 5.5f));
                var inputs = F.Inputs();
                CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
                F.Run(sim, inputs, 20);
                Assert.AreEqual(N.ConeDamage, N.Lost(sim, 1), "the first shot hits an unmarked target");
                CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
                F.Run(sim, inputs, 20);
                int expected = unlocked ? N.ConeDamage * (100 + N.NovaTalentPercent) / 100 : N.ConeDamage;
                Assert.AreEqual(expected, N.Lost(sim, 1) - N.ConeDamage, "second shot on the marked target, unlocked = " + unlocked);
            }
        }

        [Test]
        public void Ult_TeamSeesEveryEnemyFor5Seconds_EvenInBushes()
        {
            var sim = N.Create(false, N.CreateDefsOnMap(BushRow),
                N.Slot(N.Nova, 0, 2.5f, 2.5f), N.Slot(N.Dummy, 0, 1.5f, 0.5f), N.Slot(N.Dummy, 1, 8.5f, 2.5f), N.Slot(N.Dummy, 1, 14.5f, 4.5f));
            var inputs = F.Inputs();
            F.Run(sim, inputs, 1);
            Assert.IsFalse(sim.State.Fighters[2].IsVisibleTo(0));
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult);
            Assert.AreEqual(0, sim.State.Fighters[0].UltCharge, "ult spent");
            Assert.IsTrue(sim.State.Fighters[2].IsVisibleTo(0), "enemy in the bush revealed");
            Assert.IsTrue(sim.State.Fighters[3].IsVisibleTo(0), "far enemy revealed");
            Assert.AreEqual(N.RevealTicks, VisibleTicksAfter(sim, inputs, 2, 0), "5 s after the cast tick");
        }

        [Test]
        public void Ult_RevealOutlivesNovasDefeat()
        {
            var sim = N.Create(false, N.CreateDefsOnMap(BushRow), N.Slot(N.Nova, 0, 2.5f, 2.5f), N.Slot(N.Dummy, 1, 8.5f, 2.5f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult);
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 1, 0, 100000);
            Assert.IsFalse(sim.State.Fighters[0].Alive);
            F.Run(sim, inputs, 10);
            Assert.IsTrue(sim.State.Fighters[1].IsVisibleTo(0), "the team keeps the information");
        }

        [Test]
        public void Ult_BoostsExactlyTheNextThreeShots()
        {
            var sim = N.Create(false, N.CreateDefs(), N.Slot(N.Nova, 0, 2.5f, 5.5f), N.Slot(N.Dummy, 1, 8.5f, 5.5f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult);
            Assert.AreEqual(N.BoostShots, sim.State.Fighters[0].BoostedShotsLeft);

            int boosted = N.ConeDamage * (100 + N.BoostPercent) / 100;
            int lost = 0;
            for (int shot = 0; shot < 4; shot++)
            {
                if (sim.State.Fighters[0].Charges == 0) F.Run(sim, inputs, N.NovaRechargeTicks);
                CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
                F.Run(sim, inputs, 20);
                int damage = N.Lost(sim, 1) - lost;
                lost = N.Lost(sim, 1);
                Assert.AreEqual(shot < N.BoostShots ? boosted : N.ConeDamage, damage, "shot " + (shot + 1));
            }
            Assert.AreEqual(0, sim.State.Fighters[0].BoostedShotsLeft);
        }

        [Test]
        public void Flap_PushesEnemiesInRadius2_By3Cells_NotAlliesOrFarEnemies()
        {
            var sim = N.Create(N.Slot(N.Nova, 0, 10.5f, 10.5f), N.Slot(N.Dummy, 1, 11.5f, 10.5f), N.Slot(N.Dummy, 1, 10.5f, 13.5f),
                N.Slot(N.Dummy, 0, 9.5f, 10.5f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            F.Run(sim, inputs, N.FlapPushTicks + 2);

            Assert.AreEqual(11.5f + N.FlapPush, sim.State.Fighters[1].Position.X, Tolerance, "pushed 3 cells away from Nova");
            Assert.AreEqual(10.5f, sim.State.Fighters[1].Position.Y, Tolerance);
            Assert.IsTrue(Vec2.Approximately(new Vec2(10.5f, 13.5f), sim.State.Fighters[2].Position, Tolerance), "3 cells away: outside the flap");
            Assert.IsTrue(Vec2.Approximately(new Vec2(9.5f, 10.5f), sim.State.Fighters[3].Position, Tolerance), "allies stay");
            Assert.AreEqual(TestMatchFactory.TrickUses - 1, sim.State.Fighters[0].TrickUsesLeft);
        }

        [Test]
        public void Flap_WallStopsThePush()
        {
            var sim = N.Create(true, N.CreateDefsOnMap(WallColumn), N.Slot(N.Nova, 0, 2.5f, 2.5f), N.Slot(N.Dummy, 1, 3.5f, 2.5f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            F.Run(sim, inputs, N.FlapPushTicks + 2);
            Assert.AreEqual(5f - A.DummyRadius, sim.State.Fighters[1].Position.X, Tolerance, "stops at the wall face");
        }
    }
}
