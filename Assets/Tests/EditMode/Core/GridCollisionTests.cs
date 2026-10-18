using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Data.Conversion;
using NUnit.Framework;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Movement against the grid (walls, water, sliding) and the projectile ray (S2.5).</summary>
    public sealed class GridCollisionTests
    {
        private static readonly string[] WallInRow2 =
        {
            "............",
            "............",
            "......#.....",
            "............",
            "............"
        };

        private static MatchSimulation WalkerOn(string[] rows, float x, float y)
        {
            var defs = F.CreateDefsOnMap(rows);
            return F.CreateWith(defs, F.Slot(0, 0, x, y));
        }

        [TestCase('#')]
        [TestCase('~')]
        public void Fighter_DoesNotEnterWallOrWater(char blocker)
        {
            string[] rows = (string[])WallInRow2.Clone();
            rows[2] = rows[2].Replace('#', blocker);
            var sim = WalkerOn(rows, 2.5f, 2.5f);
            var inputs = F.Inputs();
            inputs[0] = new PlayerInput(Vec2.Right, Vec2.Zero, InputButtons.None);
            F.Run(sim, inputs, 90);

            Vec2 p = sim.State.Fighters[0].Position;
            Assert.AreEqual(6f - F.Radius, p.X, 1e-3f, "stops at the face of the cell");
            Assert.AreEqual(2.5f, p.Y, 1e-4f, "head-on: no sideways drift");
            Assert.IsFalse(GridCollision.OverlapsBlocking(sim.State.Grid, p, F.Radius));
            Assert.AreEqual(0f, sim.State.Fighters[0].Velocity.X, 1e-3f, "velocity is the actual displacement (0 at the wall)");
        }

        [Test]
        public void Fighter_SlidesAlongWall()
        {
            var rows = new[] { "............", "............", "............", "############" };
            var sim = WalkerOn(rows, 2.5f, 1.5f);
            var inputs = F.Inputs();
            inputs[0] = new PlayerInput(new Vec2(1f, -1f).Normalized, Vec2.Zero, InputButtons.None);
            F.Run(sim, inputs, 30);

            Vec2 p = sim.State.Fighters[0].Position;
            Assert.AreEqual(1f + F.Radius, p.Y, 1e-3f, "pressed against the wall");
            float expected = F.Speed * 0.70710678f; // tangential component over 1 s
            Assert.AreEqual(2.5f + expected, p.X, 0.02f, "keeps the tangential part of the movement");
        }

        [Test]
        public void MapEdge_IsAWall()
        {
            var sim = WalkerOn(new[] { "......", "......", "......" }, 3f, 1.5f);
            var inputs = F.Inputs();
            inputs[0] = new PlayerInput(new Vec2(-1f, 1f).Normalized, Vec2.Zero, InputButtons.None);
            F.Run(sim, inputs, 120);
            Vec2 p = sim.State.Fighters[0].Position;
            Assert.AreEqual(F.Radius, p.X, 1e-3f);
            Assert.AreEqual(3f - F.Radius, p.Y, 1e-3f);
        }

        [Test]
        public void RandomWalk_AmongObstacles_NeverOverlapsBlockingCells()
        {
            var rows = new[]
            {
                "............",
                "..##....~~..",
                "..#.....~...",
                "....%%......",
                "......#.....",
                ".~~...#..#..",
                "......#.....",
                "..#.........",
                "..#..~~~.##.",
                "............"
            };
            var defs = F.CreateDefsOnMap(rows);
            var sim = F.CreateWith(defs, F.Slot(0, 0, 0.5f, 0.5f), F.Slot(1, 1, 11.5f, 9.5f), F.Slot(0, 0, 5.5f, 0.5f), F.Slot(1, 1, 0.5f, 9.5f));
            var inputs = F.Inputs();
            DeterministicRng rng = DeterministicRng.FromSeed(5);
            for (int t = 0; t < 900; t++)
            {
                F.FillRandomInputs(ref rng, inputs, 4);
                sim.Tick(inputs);
                for (int i = 0; i < 4; i++)
                {
                    ref var f = ref sim.State.Fighters[i];
                    float radius = defs.Fighters[f.DefIndex].Radius;
                    Assert.IsFalse(GridCollision.OverlapsBlocking(sim.State.Grid, f.Position, radius), "tick " + t + ", fighter " + i + " at " + f.Position);
                }
            }
        }

        [Test]
        public void Raycast_StopsAtWall_PassesOverWater()
        {
            MapDef map = MapConverter.FromRows("ray", new[] { "............", "....~.#.....", "............" });
            TileGrid grid = map.InitialGrid;
            Assert.IsTrue(GridCollision.Raycast(in grid, new Vec2(0.5f, 1.5f), Vec2.Right, 20f, out float d, out GridCell cell));
            Assert.AreEqual(5.5f, d, 1e-5f);
            Assert.AreEqual(new GridCell(6, 1), cell);

            Assert.IsFalse(GridCollision.Raycast(in grid, new Vec2(0.5f, 1.5f), Vec2.Right, 5f, out _, out _), "short ray ends before the wall");
            Assert.IsFalse(GridCollision.Raycast(in grid, new Vec2(0.5f, 0.5f), Vec2.Right, 11f, out _, out _), "open row");
            Assert.IsTrue(GridCollision.Raycast(in grid, new Vec2(4.5f, 0.2f), new Vec2(1f, 1f).Normalized, 5f, out float dd, out GridCell diag));
            Assert.AreEqual(new GridCell(6, 1), diag, "diagonal ray enters the wall cell");
            Assert.AreEqual(1.5f * 1.41421356f, dd, 1e-4f, "enters at x = 6");
            Assert.IsTrue(GridCollision.HasLineOfSight(in grid, new Vec2(0.5f, 1.5f), new Vec2(5.5f, 1.5f)), "water does not block sight");
            Assert.IsFalse(GridCollision.HasLineOfSight(in grid, new Vec2(0.5f, 1.5f), new Vec2(8.5f, 1.5f)));
        }
    }
}
