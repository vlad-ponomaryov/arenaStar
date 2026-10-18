using Arena.Bots.Pathfinding;
using Arena.Core.Map;
using Arena.Core.Sim;
using Arena.Data.Conversion;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Bots
{
    /// <summary>A* over the map grid (GDD §7): detours, closed areas, water, corner cutting, limits, no allocations.</summary>
    public sealed class GridPathfinderTests
    {
        private const int MaxLength = 128;

        private static TileGrid Grid(params string[] rows) => MapConverter.FromRows("path_test", rows).InitialGrid;

        private static GridPathfinder Pathfinder() => new GridPathfinder(SimLimits.MaxMapCells, SimLimits.MaxMapCells);

        /// <summary>Checks the path is a chain of 8-neighbour steps over walkable cells without corner cutting, ending at the goal.</summary>
        private static void AssertValidPath(in TileGrid grid, GridCell start, GridCell goal, GridCell[] path, int length)
        {
            Assert.Greater(length, 0);
            Assert.AreEqual(goal, path[length - 1], "ends at the goal");
            GridCell prev = start;
            for (int i = 0; i < length; i++)
            {
                GridCell c = path[i];
                Assert.IsFalse(grid.BlocksMovement(c.Column, c.Row), "cell " + c + " is walkable");
                int dx = c.Column - prev.Column, dy = c.Row - prev.Row;
                Assert.IsTrue(dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1 && (dx != 0 || dy != 0), "step " + prev + " → " + c);
                if (dx != 0 && dy != 0)
                {
                    Assert.IsFalse(grid.BlocksMovement(prev.Column + dx, prev.Row), "no corner cutting at " + prev + " → " + c);
                    Assert.IsFalse(grid.BlocksMovement(prev.Column, prev.Row + dy), "no corner cutting at " + prev + " → " + c);
                }
                prev = c;
            }
        }

        [Test]
        public void GoesAroundAWall()
        {
            TileGrid grid = Grid(
                "..........",
                "....#.....",
                "....#.....",
                "....#.....",
                "....#.....");
            var path = new GridCell[MaxLength];
            var start = new GridCell(1, 0);
            var goal = new GridCell(8, 0);

            Assert.IsTrue(Pathfinder().FindPath(grid, start, goal, path, 0, MaxLength, out int length));

            AssertValidPath(grid, start, goal, path, length);
            Assert.Greater(length, 7, "longer than the straight line through the wall");
            bool overTheWall = false;
            for (int i = 0; i < length; i++) overTheWall |= path[i].Row == 4;
            Assert.IsTrue(overTheWall, "passes above the wall (row 4)");
        }

        [Test]
        public void ClosedArea_HasNoPath()
        {
            TileGrid grid = Grid(
                ".........",
                "...###...",
                "...#.#...",
                "...###...",
                ".........");
            var path = new GridCell[MaxLength];

            Assert.IsFalse(Pathfinder().FindPath(grid, new GridCell(0, 0), new GridCell(4, 2), path, 0, MaxLength, out int length));
            Assert.AreEqual(0, length);
        }

        [Test]
        public void Water_IsImpassable()
        {
            // A river across the map with one bridge at column 7.
            TileGrid bridge = Grid(
                "..........",
                "..........",
                "~~~~~~~.~~",
                "..........",
                "..........");
            var path = new GridCell[MaxLength];
            var start = new GridCell(1, 0);
            var goal = new GridCell(1, 4);
            Assert.IsTrue(Pathfinder().FindPath(bridge, start, goal, path, 0, MaxLength, out int length));
            AssertValidPath(bridge, start, goal, path, length);
            bool usesBridge = false;
            for (int i = 0; i < length; i++) usesBridge |= path[i] == new GridCell(7, 2);
            Assert.IsTrue(usesBridge, "crosses at the bridge");

            TileGrid river = Grid(
                "..........",
                "~~~~~~~~~~",
                "..........");
            Assert.IsFalse(Pathfinder().FindPath(river, new GridCell(1, 0), new GridCell(1, 2), path, 0, MaxLength, out _));
        }

        [Test]
        public void DiagonalBetweenTwoWalls_IsNotTaken()
        {
            // (0,1) and (1,0) are walls: the only connection (0,0) → (1,1) would cut both corners.
            TileGrid grid = Grid(
                "#.",
                ".#");
            var path = new GridCell[MaxLength];
            Assert.IsFalse(Pathfinder().FindPath(grid, new GridCell(0, 0), new GridCell(1, 1), path, 0, MaxLength, out _));
        }

        [Test]
        public void OpenField_DiagonalPathIsShortest()
        {
            TileGrid grid = MapDefGrid(10, 10);
            var path = new GridCell[MaxLength];
            Assert.IsTrue(Pathfinder().FindPath(grid, new GridCell(0, 0), new GridCell(5, 5), path, 0, MaxLength, out int length));
            Assert.AreEqual(5, length, "five diagonal steps");
        }

        [Test]
        public void StartEqualsGoal_EmptyPath_BlockedGoal_NoPath()
        {
            TileGrid grid = Grid(
                "...",
                ".#.",
                "...");
            var path = new GridCell[MaxLength];
            GridPathfinder finder = Pathfinder();
            Assert.IsTrue(finder.FindPath(grid, new GridCell(0, 0), new GridCell(0, 0), path, 0, MaxLength, out int length));
            Assert.AreEqual(0, length);
            Assert.IsFalse(finder.FindPath(grid, new GridCell(0, 0), new GridCell(1, 1), path, 0, MaxLength, out _));
            Assert.IsFalse(finder.FindPath(grid, new GridCell(0, 0), new GridCell(5, 5), path, 0, MaxLength, out _), "goal outside");
        }

        [Test]
        public void LongPath_IsCutToMaxLength_AtTheOffset()
        {
            TileGrid grid = MapDefGrid(21, 31);
            var path = new GridCell[20];
            Assert.IsTrue(Pathfinder().FindPath(grid, new GridCell(10, 0), new GridCell(10, 30), path, 10, 10, out int length));
            Assert.AreEqual(10, length);
            Assert.AreEqual(default(GridCell), path[9], "nothing written before the offset");
            for (int i = 0; i < 10; i++) Assert.AreEqual(new GridCell(10, i + 1), path[10 + i], "first cells from the start");
        }

        [Test]
        public void NodeLimit_StopsTheSearch()
        {
            TileGrid grid = MapDefGrid(21, 31);
            var path = new GridCell[MaxLength];
            var finder = new GridPathfinder(SimLimits.MaxMapCells, 5);
            Assert.IsFalse(finder.FindPath(grid, new GridCell(0, 0), new GridCell(20, 30), path, 0, MaxLength, out _));
            Assert.LessOrEqual(finder.LastExpanded, 5);
        }

        [Test]
        public void RepeatedSearches_GiveTheSameResult_AndDoNotAllocate()
        {
            var defs = TestMatchFactory.CreateDefsOnMap(
                ".....................",
                "..###....~~~....###..",
                "....#....~~~....#....",
                "....#...........#....",
                "..%%%%%.......%%%%%..",
                ".....................",
                "...~~~~~~...~~~~~~...",
                ".....................");
            TileGrid grid = defs.Map.InitialGrid;
            var path = new GridCell[MaxLength];
            GridPathfinder finder = Pathfinder();
            var start = new GridCell(0, 0);
            var goal = new GridCell(20, 7);
            Assert.IsTrue(finder.FindPath(grid, start, goal, path, 0, MaxLength, out int first));
            AssertValidPath(grid, start, goal, path, first);

            int length = 0;
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 200; i++) finder.FindPath(grid, start, goal, path, 0, MaxLength, out length);
            }, "200 searches");
            Assert.AreEqual(first, length);
        }

        private static TileGrid MapDefGrid(int width, int height) => Arena.Core.Map.MapDef.CreateEmpty(width, height).InitialGrid;
    }
}
