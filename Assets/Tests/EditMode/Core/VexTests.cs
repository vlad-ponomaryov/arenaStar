using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;
using N = Arena.Tests.EditMode.Core.ScoutFighterTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Vex (S2.12): double strike, hook (wall / enemy), smoke invisibility, charges refilled on a kill — data + shared components.</summary>
    public sealed class VexTests
    {
        private const float Tolerance = 0.06f;

        // Wall column at x = 8 (16 × 5).
        private static readonly string[] WallColumn =
        {
            "........#.......",
            "........#.......",
            "........#.......",
            "........#.......",
            "........#......."
        };

        private static void RunUntilStill(MatchSimulation sim, PlayerInput[] inputs, int slot, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                sim.Tick(inputs);
                if (i > 2 && !sim.State.Fighters[slot].IsDashing && ProjectilesActive(sim) == 0) return;
            }
        }

        private static int ProjectilesActive(MatchSimulation sim)
        {
            int n = 0;
            for (int i = 0; i < sim.State.Projectiles.Length; i++)
                if (sim.State.Projectiles[i].Active) n++;
            return n;
        }

        [Test]
        public void Attack_TwoClawStrikes_OneCharge()
        {
            var sim = N.Create(false, N.CreateDefs(), N.Slot(N.Vex, 0, 5.5f, 5.5f), N.Slot(N.Dummy, 1, 6.7f, 5.5f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            Assert.AreEqual(N.ClawDamage, N.Lost(sim, 1), "first strike at once");
            Assert.AreEqual(2, sim.State.Fighters[0].Charges);
            F.Run(sim, inputs, N.ClawIntervalTicks - 1);
            Assert.AreEqual(N.ClawDamage, N.Lost(sim, 1), "the second strike waits for the interval");
            F.Run(sim, inputs, 1);
            Assert.AreEqual(N.ClawHits * N.ClawDamage, N.Lost(sim, 1), "two strikes of 520");
            F.Run(sim, inputs, 10);
            Assert.AreEqual(N.ClawHits * N.ClawDamage, N.Lost(sim, 1), "no third strike");
            Assert.AreEqual(2, sim.State.Fighters[0].Charges, "one charge for both strikes");
        }

        [Test]
        public void Hook_IntoWall_PullsVexToTheWall()
        {
            var sim = N.Create(false, N.CreateDefsOnMap(WallColumn), N.Slot(N.Vex, 0, 2.5f, 2.5f), N.Slot(N.Dummy, 1, 2.5f, 0.5f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Right);
            RunUntilStill(sim, inputs, 0, 60);
            Assert.AreEqual(8f - N.VexRadius, sim.State.Fighters[0].Position.X, Tolerance, "body touches the wall face");
            Assert.AreEqual(2.5f, sim.State.Fighters[0].Position.Y, Tolerance);
            Assert.AreEqual(0, N.Lost(sim, 1));
        }

        [Test]
        public void Hook_IntoEnemy_PullsVexToIt_DamagesAndSlows()
        {
            var sim = N.Create(false, N.CreateDefs(), N.Slot(N.Vex, 0, 2.5f, 5.5f), N.Slot(N.Dummy, 1, 8.5f, 5.5f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Right);
            int hitTick = -1;
            for (int i = 0; i < 30 && hitTick < 0; i++)
            {
                sim.Tick(inputs);
                if (N.Lost(sim, 1) > 0) hitTick = i;
            }
            Assert.GreaterOrEqual(hitTick, 0, "the hook reaches the enemy");
            Assert.AreEqual(N.HookDamage, N.Lost(sim, 1));
            Assert.AreEqual(1f - N.HookSlowPercent / 100f, sim.State.Fighters[1].SpeedMul, 1e-5f, "slowed by 30 %");
            Assert.IsTrue(sim.State.Fighters[0].IsDashing, "pulled towards the enemy");

            RunUntilStill(sim, inputs, 0, 60);
            float gap = Vec2.Distance(sim.State.Fighters[0].Position, sim.State.Fighters[1].Position);
            Assert.AreEqual(N.VexRadius + A.DummyRadius, gap, Tolerance, "ends next to the enemy");
            F.Run(sim, inputs, N.HookSlowTicks + 2);
            Assert.AreEqual(1f, sim.State.Fighters[1].SpeedMul, 1e-5f, "slow lasts 1 s");
        }

        [Test]
        public void Hook_Miss_DoesNotMoveVex()
        {
            var sim = N.Create(false, N.CreateDefs(), N.Slot(N.Vex, 0, 10.5f, 5.5f), N.Slot(N.Dummy, 1, 2.5f, 20.5f));
            var inputs = F.Inputs();
            A.ChargeUlt(sim, 0);
            A.Press(sim, inputs, 0, InputButtons.Ult, Vec2.Up);
            RunUntilStill(sim, inputs, 0, 60);
            Assert.IsTrue(Vec2.Approximately(new Vec2(10.5f, 5.5f), sim.State.Fighters[0].Position, 1e-4f), "7 cells of open floor: no pull");
        }

        [Test]
        public void Smoke_HidesEvenUpClose_AttackEndsIt()
        {
            var sim = N.Create(N.Slot(N.Vex, 0, 5.5f, 5.5f), N.Slot(N.Dummy, 1, 6.5f, 5.5f));
            var inputs = F.Inputs();
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.IsTrue(sim.State.Fighters[0].Invisible);
            Assert.IsFalse(sim.State.Fighters[0].IsVisibleTo(1), "invisible even 1 cell from the enemy (D-024)");
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(0), "allies see it");

            F.Run(sim, inputs, 10);
            Assert.IsFalse(sim.State.Fighters[0].IsVisibleTo(1));
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            Assert.IsFalse(sim.State.Fighters[0].Invisible, "attacking ends the smoke");
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(1));
        }

        [Test]
        public void Smoke_RightAfterAnAttack_HidesAtOnce()
        {
            var sim = N.Create(N.Slot(N.Vex, 0, 5.5f, 5.5f), N.Slot(N.Dummy, 1, 6.5f, 5.5f));
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            F.Run(sim, inputs, N.ClawIntervalTicks + 1);
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(1), "revealed by the attack");
            A.Press(sim, inputs, 0, InputButtons.Trick);
            Assert.IsFalse(sim.State.Fighters[0].IsVisibleTo(1), "the smoke ends the attack reveal (D-061)");
            F.Run(sim, inputs, N.SmokeTicks + 2);
            Assert.IsFalse(sim.State.Fighters[0].Invisible, "1.5 s");
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Talent_DefeatRefillsAllCharges_OnlyWhenUnlocked(bool unlocked)
        {
            var sim = N.Create(unlocked, N.CreateDefs(), N.Slot(N.Vex, 0, 5.5f, 5.5f), N.Slot(N.Dummy, 1, 6.7f, 5.5f));
            var inputs = F.Inputs();
            sim.State.Fighters[1].Health = 1500; // two strikes leave 460, the third one defeats
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            F.Run(sim, inputs, N.ClawIntervalTicks + 1);
            Assert.AreEqual(2, sim.State.Fighters[0].Charges);
            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Right);
            Assert.IsFalse(sim.State.Fighters[1].Alive, "defeated by the first strike of the second attack");
            Assert.AreEqual(unlocked ? 3 : 1, sim.State.Fighters[0].Charges, unlocked ? "refilled" : "talent locked");
        }
    }
}
