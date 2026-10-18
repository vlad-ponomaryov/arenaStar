using Arena.Bots;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Bots
{
    /// <summary>What a bot may know (D-022): enemies visible to its team within the view radius; line of fire.</summary>
    public sealed class PerceptionTests
    {
        // 21×21 floor with a 3×3 bush around (10, 15).
        private static MatchDefs BushDefs() => BotTestFactory.DefsOnRows(BotTestFactory.RowsWithBlock(21, 21, '"', 9, 14, 11, 16));

        [Test]
        public void EnemyInBush_IsNotSeen_UntilCloserThanTwoCells()
        {
            MatchSimulation sim = TestMatchFactory.CreateWith(BushDefs(),
                TestMatchFactory.Slot(0, 0, 10.5f, 10.5f), TestMatchFactory.Slot(0, 1, 10.5f, 15.5f));

            Assert.IsFalse(Perception.CanSee(sim.State, 0, 1, BotTestFactory.ViewRadius), "5 cells from a bush: hidden");
            Assert.AreEqual(-1, Perception.FindNearestEnemy(sim.State, 0, BotTestFactory.ViewRadius, out _));

            sim.State.Fighters[0].Position = new Vec2(10.5f, 13.8f); // 1.7 cells: the bush reveals it (D-024)
            Arena.Core.Sim.Systems.VisibilitySystem.Run(sim.State, sim.Defs);
            Assert.IsTrue(Perception.CanSee(sim.State, 0, 1, BotTestFactory.ViewRadius));
        }

        [Test]
        public void OnlyEnemies_AliveAndWithinViewRadius()
        {
            MatchSimulation sim = TestMatchFactory.CreateWith(TestMatchFactory.CreateDefs(),
                TestMatchFactory.Slot(0, 0, 10.5f, 5.5f), TestMatchFactory.Slot(0, 0, 10.5f, 7.5f),
                TestMatchFactory.Slot(0, 1, 10.5f, 14.5f), TestMatchFactory.Slot(0, 1, 10.5f, 25.5f));

            Assert.IsFalse(Perception.CanSee(sim.State, 0, 1, BotTestFactory.ViewRadius), "ally");
            Assert.IsTrue(Perception.CanSee(sim.State, 0, 2, BotTestFactory.ViewRadius), "enemy 9 cells away");
            Assert.IsFalse(Perception.CanSee(sim.State, 0, 3, BotTestFactory.ViewRadius), "enemy 20 cells away — off screen");
            Assert.AreEqual(2, Perception.FindNearestEnemy(sim.State, 0, BotTestFactory.ViewRadius, out float d));
            Assert.AreEqual(9f, d, 1e-4f);

            sim.State.Fighters[2].Alive = false;
            Assert.IsFalse(Perception.CanSee(sim.State, 0, 2, BotTestFactory.ViewRadius), "defeated");
        }

        [Test]
        public void ClearShot_BlockedByWalls_NotByWaterOrBushes()
        {
            TileGrid grid = BotTestFactory.DefsOnRows(new[]
            {
                ".........",
                "....#....",
                ".........",
                "....~....",
                ".........",
                "....\"....",
                "........."
            }).Map.InitialGrid;

            Assert.IsFalse(Perception.HasClearShot(grid, new Vec2(1.5f, 5.5f), new Vec2(7.5f, 5.5f)), "wall in row 5");
            Assert.IsTrue(Perception.HasClearShot(grid, new Vec2(1.5f, 3.5f), new Vec2(7.5f, 3.5f)), "over water");
            Assert.IsTrue(Perception.HasClearShot(grid, new Vec2(1.5f, 1.5f), new Vec2(7.5f, 1.5f)), "through a bush");
            Assert.IsFalse(Perception.HasClearShot(grid, new Vec2(4.5f, 3.5f), new Vec2(4.5f, 6.5f)), "vertical through the wall");
            Assert.IsTrue(Perception.HasClearShot(grid, new Vec2(1.2f, 0.5f), new Vec2(1.7f, 6.5f)), "steep line beside the wall");
        }

        [Test]
        public void Objective_NearestAcorn_ElseHollow_ElseMapCentre()
        {
            MatchDefs acorns = AcornsTestFactory.Defs();
            MatchSimulation sim = TestMatchFactory.CreateWith(acorns, TestMatchFactory.Slot(0, 0, 5.5f, 5.5f));
            Assert.IsTrue(Perception.FindObjective(sim.State, acorns, new Vec2(5.5f, 5.5f), out Vec2 p));
            Assert.AreEqual(AcornsTestFactory.HollowCell.Center, p, "no acorns: the hollow");

            AcornsTestFactory.Place(sim.State, 0, new Vec2(15.5f, 20.5f));
            AcornsTestFactory.Place(sim.State, 1, new Vec2(5.5f, 9.5f));
            Assert.IsTrue(Perception.FindObjective(sim.State, acorns, new Vec2(5.5f, 5.5f), out p));
            Assert.AreEqual(new Vec2(5.5f, 9.5f), p, "nearest acorn");

            MatchDefs free = TestMatchFactory.CreateDefs();
            MatchSimulation freeSim = TestMatchFactory.CreateWith(free, TestMatchFactory.Slot(0, 0, 5.5f, 5.5f));
            Assert.IsTrue(Perception.FindObjective(freeSim.State, free, Vec2.Zero, out p));
            Assert.AreEqual(free.Map.Center, p, "no mode: map centre");
        }
    }
}
