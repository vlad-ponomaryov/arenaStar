using System;
using Arena.Core.Map;

namespace Arena.Bots.Pathfinding
{
    /// <summary>
    /// A* over the map grid (GDD §7): 8 neighbours, a diagonal step only if both orthogonal neighbours are free (no corner
    /// cutting — a body of radius &lt; 0.5 then never scrapes a wall corner), cost 1 / √2, octile heuristic. Passability is
    /// <see cref="TileGrid.BlocksMovement"/> of the current tiles (broken walls open new paths; water is impassable).
    /// All buffers — scores, parents, the binary heap with positions for decrease-key — are allocated once in the
    /// constructor; "visited" is a stamp per search, so nothing is cleared and a search allocates nothing.
    /// A search stops without a path when the goal is unreachable or <see cref="NodeLimit"/> nodes were expanded.
    /// </summary>
    public sealed class GridPathfinder
    {
        private const float Diagonal = 1.41421356f; // √2: cost of a diagonal step on a unit grid

        private static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] StepY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private readonly float[] _g;
        private readonly float[] _f;
        private readonly int[] _parent;
        private readonly int[] _visitStamp;
        private readonly int[] _closedStamp;
        private readonly int[] _heap;
        private readonly int[] _heapPos;
        private int _heapCount;
        private int _stamp;

        /// <summary>Cells the buffers hold (the largest map, <c>SimLimits.MaxMapCells</c>).</summary>
        public int Capacity { get; }

        /// <summary>Maximum nodes expanded per search.</summary>
        public int NodeLimit { get; set; }

        /// <summary>Nodes expanded by the last search (diagnostics, tests).</summary>
        public int LastExpanded { get; private set; }

        public GridPathfinder(int capacity, int nodeLimit)
        {
            if (capacity < 1) throw new ArgumentException("GridPathfinder: capacity must be ≥ 1");
            if (nodeLimit < 1) throw new ArgumentException("GridPathfinder: nodeLimit must be ≥ 1");
            Capacity = capacity;
            NodeLimit = nodeLimit;
            _g = new float[capacity];
            _f = new float[capacity];
            _parent = new int[capacity];
            _visitStamp = new int[capacity];
            _closedStamp = new int[capacity];
            _heap = new int[capacity];
            _heapPos = new int[capacity];
        }

        /// <summary>
        /// Finds a path from <paramref name="start"/> to <paramref name="goal"/> and writes its cells after the start (the goal
        /// last) to <paramref name="path"/>[offset …]. A path longer than <paramref name="maxLength"/> is cut to its first
        /// <paramref name="maxLength"/> cells. The start cell itself is searched from even if it is blocked (a body pushed
        /// against a wall). Returns false (length 0) if there is no path within the node limit; start = goal gives true, 0.
        /// </summary>
        public bool FindPath(in TileGrid grid, GridCell start, GridCell goal, GridCell[] path, int offset, int maxLength, out int length)
        {
            length = 0;
            int width = grid.Width;
            int cells = width * grid.Height;
            if (cells > Capacity) throw new ArgumentException("GridPathfinder: map of " + cells + " cells > capacity " + Capacity);
            if (!grid.InBounds(start.Column, start.Row) || !grid.InBounds(goal.Column, goal.Row)) return false;
            if (start == goal) return true;
            if (grid.BlocksMovement(goal.Column, goal.Row)) return false;

            NextStamp();
            _heapCount = 0;
            LastExpanded = 0;
            int startIndex = grid.IndexOf(start.Column, start.Row);
            int goalIndex = grid.IndexOf(goal.Column, goal.Row);
            Visit(startIndex, -1, 0f, Heuristic(start.Column, start.Row, goal));
            Push(startIndex);

            while (_heapCount > 0)
            {
                int current = Pop();
                if (current == goalIndex)
                {
                    length = WritePath(goalIndex, width, path, offset, maxLength);
                    return true;
                }
                if (LastExpanded >= NodeLimit) return false;
                LastExpanded++;
                _closedStamp[current] = _stamp;

                int cx = current % width;
                int cy = current / width;
                for (int k = 0; k < StepX.Length; k++)
                {
                    int nx = cx + StepX[k];
                    int ny = cy + StepY[k];
                    if (grid.BlocksMovement(nx, ny)) continue; // outside the map reads as a wall
                    bool diagonal = StepX[k] != 0 && StepY[k] != 0;
                    if (diagonal && (grid.BlocksMovement(cx + StepX[k], cy) || grid.BlocksMovement(cx, cy + StepY[k]))) continue;

                    int next = ny * width + nx;
                    if (_closedStamp[next] == _stamp) continue;
                    float g = _g[current] + (diagonal ? Diagonal : 1f);
                    if (_visitStamp[next] != _stamp)
                    {
                        Visit(next, current, g, g + Heuristic(nx, ny, goal));
                        Push(next);
                    }
                    else if (g < _g[next])
                    {
                        Visit(next, current, g, g + Heuristic(nx, ny, goal));
                        SiftUp(_heapPos[next]);
                    }
                }
            }
            return false;
        }

        private void NextStamp()
        {
            if (_stamp == int.MaxValue)
            {
                Array.Clear(_visitStamp, 0, _visitStamp.Length);
                Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _stamp = 0;
            }
            _stamp++;
        }

        private void Visit(int node, int parent, float g, float f)
        {
            _visitStamp[node] = _stamp;
            _parent[node] = parent;
            _g[node] = g;
            _f[node] = f;
        }

        /// <summary>Octile distance: exact cost on an empty 8-connected grid (admissible and consistent).</summary>
        private static float Heuristic(int x, int y, GridCell goal)
        {
            int dx = x - goal.Column;
            int dy = y - goal.Row;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;
            int min = dx < dy ? dx : dy;
            return dx + dy + (Diagonal - 2f) * min;
        }

        /// <summary>Walks the parents back from the goal; the cell at depth d (start = 0) goes to path[offset + d − 1].</summary>
        private int WritePath(int goalIndex, int width, GridCell[] path, int offset, int maxLength)
        {
            int total = 0;
            for (int n = goalIndex; _parent[n] >= 0; n = _parent[n]) total++;
            int length = total < maxLength ? total : maxLength;
            int depth = total;
            for (int n = goalIndex; _parent[n] >= 0; n = _parent[n], depth--)
                if (depth <= length) path[offset + depth - 1] = new GridCell(n % width, n / width);
            return length;
        }

        // --- Binary min-heap on _f with positions (_heapPos) for decrease-key. ---

        private void Push(int node)
        {
            int i = _heapCount++;
            _heap[i] = node;
            _heapPos[node] = i;
            SiftUp(i);
        }

        private int Pop()
        {
            int top = _heap[0];
            int last = _heap[--_heapCount];
            if (_heapCount > 0)
            {
                _heap[0] = last;
                _heapPos[last] = 0;
                SiftDown(0);
            }
            return top;
        }

        private void SiftUp(int i)
        {
            int node = _heap[i];
            float key = _f[node];
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                int p = _heap[parent];
                if (_f[p] <= key) break;
                _heap[i] = p;
                _heapPos[p] = i;
                i = parent;
            }
            _heap[i] = node;
            _heapPos[node] = i;
        }

        private void SiftDown(int i)
        {
            int node = _heap[i];
            float key = _f[node];
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= _heapCount) break;
                if (child + 1 < _heapCount && _f[_heap[child + 1]] < _f[_heap[child]]) child++;
                int c = _heap[child];
                if (_f[c] >= key) break;
                _heap[i] = c;
                _heapPos[c] = i;
                i = child;
            }
            _heap[i] = node;
            _heapPos[node] = i;
        }
    }
}
