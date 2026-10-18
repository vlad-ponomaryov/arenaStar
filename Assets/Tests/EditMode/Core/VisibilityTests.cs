using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Systems;
using NUnit.Framework;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Bushes and visibility (D-024, GDD §5.2).</summary>
    public sealed class VisibilityTests
    {
        // 12×12, bush block in columns 5–7, rows 5–7 (centre cell (6, 6)).
        private static readonly string[] BushMap =
        {
            "............",
            "............",
            "............",
            "............",
            ".....\"\"\"....",
            ".....\"\"\"....",
            ".....\"\"\"....",
            "............",
            "............",
            "............",
            "............",
            "............"
        };

        /// <summary>Slot 0: hider (cone fighter, team 1) in the bush centre; slot 1: observer (team 0) at (x, y).</summary>
        private static MatchSimulation HiderAndObserver(float observerX, float observerY) =>
            F.CreateWith(F.CreateDefsOnMap(BushMap), F.Slot(1, 1, 6.5f, 6.5f), F.Slot(0, 0, observerX, observerY));

        [Test]
        public void EnemyInBush_IsHiddenFromOtherTeam_VisibleToOwnTeam()
        {
            var sim = HiderAndObserver(6.5f, 1.5f);
            ref var hider = ref sim.State.Fighters[0];
            Assert.IsTrue(hider.InBush);
            Assert.IsFalse(hider.IsVisibleTo(0), "observer 5 cells away does not see into the bush");
            Assert.IsTrue(hider.IsVisibleTo(1), "own team always sees");
            Assert.IsTrue(sim.State.Fighters[1].IsVisibleTo(1), "a fighter outside a bush is seen by everyone");
        }

        [Test]
        public void EnemyCloserThanTwoCells_SeesIntoBush()
        {
            Assert.IsTrue(HiderAndObserver(6.5f, 4.6f).State.Fighters[0].IsVisibleTo(0), "1.9 cells");
            Assert.IsFalse(HiderAndObserver(6.5f, 4.5f).State.Fighters[0].IsVisibleTo(0), "exactly 2 cells is not closer than 2");

            var sim = HiderAndObserver(6.5f, 3.5f);
            Assert.IsFalse(sim.State.Fighters[0].IsVisibleTo(0));
            var inputs = F.Inputs();
            inputs[1] = new PlayerInput(Vec2.Up, Vec2.Zero, InputButtons.None);
            int ticks = 0;
            while (!sim.State.Fighters[0].IsVisibleTo(0) && ticks < 60)
            {
                sim.Tick(inputs);
                ticks++;
            }
            Assert.Less(Vec2.Distance(sim.State.Fighters[0].Position, sim.State.Fighters[1].Position), F.BushRevealDistance);
            Assert.Greater(Vec2.Distance(sim.State.Fighters[0].Position, sim.State.Fighters[1].Position), F.BushRevealDistance - 0.1f,
                "revealed as soon as the observer comes closer than 2 cells");
        }

        [Test]
        public void Attack_RevealsForOneSecond()
        {
            var sim = HiderAndObserver(6.5f, 1.5f);
            var inputs = F.Inputs();
            CombatTestUtil.PressAttack(sim, inputs, 0, new Vec2(0f, -1f));
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(0), "visible right after attacking");
            F.Run(sim, inputs, F.RevealTicks - 1);
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(0), "still visible in the 30th tick");
            sim.Tick(inputs);
            Assert.IsFalse(sim.State.Fighters[0].IsVisibleTo(0), "hidden again after 1 s");
        }

        [Test]
        public void Allies_AlwaysSeeEachOther()
        {
            var sim = F.CreateWith(F.CreateDefsOnMap(BushMap), F.Slot(1, 1, 6.5f, 6.5f), F.Slot(0, 1, 0.5f, 11.5f), F.Slot(0, 0, 11.5f, 0.5f));
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(1));
            Assert.IsFalse(sim.State.Fighters[0].IsVisibleTo(0));
        }

        [Test]
        public void Invisible_IsNotRevealedByProximity_ButByAttack()
        {
            // Hider outside any bush, observer 1 cell away.
            var sim = F.CreateWith(F.CreateDefsOnMap(BushMap), F.Slot(1, 1, 2.5f, 2.5f), F.Slot(0, 0, 3.5f, 2.5f));
            StatusSystem.Apply(sim.State, sim.Events, 0, 0, 1, EffectKind.Invisibility, 0, 300);
            var inputs = F.Inputs();
            sim.Tick(inputs);
            Assert.IsTrue(sim.State.Fighters[0].Invisible);
            Assert.IsFalse(sim.State.Fighters[0].IsVisibleTo(0), "invisible even next to the enemy");

            CombatTestUtil.PressAttack(sim, inputs, 0, Vec2.Up);
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(0), "attacking reveals and ends invisibility");
            Assert.IsFalse(sim.State.Fighters[0].Invisible);
        }

        [Test]
        public void Mark_MakesVisibleToTheMarkingTeam()
        {
            var sim = HiderAndObserver(6.5f, 1.5f);
            StatusSystem.Apply(sim.State, sim.Events, 0, 1, 0, EffectKind.Mark, 0, 120);
            sim.Tick(F.Inputs());
            Assert.IsTrue(sim.State.Fighters[0].IsVisibleTo(0));
        }

        [Test]
        public void AutoAim_IgnoresHiddenEnemies()
        {
            // Shooter (team 0) at (6.5, 2.5); hidden enemy in the bush 4 cells up; visible enemy 5 cells left.
            var sim = F.CreateWith(F.CreateDefsOnMap(BushMap),
                F.Slot(0, 0, 6.5f, 2.5f), F.Slot(1, 1, 6.5f, 6.5f), F.Slot(1, 1, 1.5f, 2.5f));
            Assert.AreEqual(2, TargetingSystem.FindNearestEnemy(sim.State, 0, F.ShooterRange), "the nearer enemy is hidden");

            var alone = F.CreateWith(F.CreateDefsOnMap(BushMap), F.Slot(0, 0, 6.5f, 2.5f), F.Slot(1, 1, 6.5f, 6.5f));
            Assert.AreEqual(-1, TargetingSystem.FindNearestEnemy(alone.State, 0, F.ShooterRange));
            var input = new PlayerInput(Vec2.Zero, Vec2.Zero, InputButtons.AutoAim);
            Vec2 facing = alone.State.Fighters[0].Facing;
            Assert.AreEqual(facing, TargetingSystem.ResolveAim(alone.State, 0, in input, F.ShooterRange), "falls back to facing");
        }
    }
}
