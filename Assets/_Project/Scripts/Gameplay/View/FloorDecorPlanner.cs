using System.Collections.Generic;
using Arena.Bots.Pathfinding;
using Arena.Core.Map;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Plans the floor details of a map (A.3e, D-130): stepping-stone paths along A* routes between anchor cells
    /// (<see cref="EnvDecorStyle.Paths"/>, map fractions) and rare pebbles, sprouts and flowers on free floor cells.
    /// Deterministic (seed = hash of the tiles) and centrally symmetric: every item has its twin turned 180° about the map centre,
    /// so both team cameras see the same arena (brief §3.7); an item is kept only if its twin fits too. Nothing on walls, crates,
    /// water, bushes or berry boxes. Runs once at map load (allocates there only).
    /// </summary>
    public static class FloorDecorPlanner
    {
        private const int SmoothingPasses = 2;

        /// <summary>Plans the details into <paramref name="items"/> (cleared first); returns their triangle count.</summary>
        public static int Plan(in TileGrid grid, in EnvDecorStyle style, List<FloorDecorItem> items)
        {
            items.Clear();
            uint seed = Seed(in grid);
            int triangles = 0;
            int budget = style.MaxTriangles > 0 ? style.MaxTriangles : int.MaxValue;
            if (style.Paths != null && style.PathStep > 0f)
            {
                var pathfinder = new GridPathfinder(grid.Width * grid.Height, grid.Width * grid.Height);
                var cells = new GridCell[grid.Width * grid.Height];
                var line = new List<Vector2>();
                for (int p = 0; p < style.Paths.Length; p++)
                {
                    Vector4 path = style.Paths[p];
                    if (!NearestFree(in grid, path.x, path.y, out GridCell from) || !NearestFree(in grid, path.z, path.w, out GridCell to)) continue;
                    if (!pathfinder.FindPath(in grid, from, to, cells, 0, cells.Length, out int length) || length == 0) continue;
                    line.Clear();
                    line.Add(Centre(from));
                    for (int i = 0; i < length; i++) line.Add(Centre(cells[i]));
                    Smooth(line);
                    triangles = PlaceStones(in grid, in style, line, seed + (uint)p * 7919u, items, triangles, budget);
                }
            }
            return Scatter(in grid, in style, seed, items, triangles, budget);
        }

        /// <summary>Mirror of a world XZ point through the map centre.</summary>
        public static Vector2 Mirror(Vector2 p, int width, int height) => new Vector2(width - p.x, height - p.y);

        private static int PlaceStones(in TileGrid grid, in EnvDecorStyle style, List<Vector2> line, uint seed,
            List<FloorDecorItem> items, int triangles, int budget)
        {
            int tri = FloorDecorMeshBuilder.TrianglesOf(FloorDecorKind.Stone);
            float carry = 0f;
            int step = 0;
            for (int s = 0; s + 1 < line.Count; s++)
            {
                Vector2 a = line[s], b = line[s + 1];
                float len = Vector2.Distance(a, b);
                if (len < 1e-4f) continue;
                Vector2 dir = (b - a) / len, normal = new Vector2(-dir.y, dir.x);
                float t = carry;
                for (; t < len; t += style.PathStep)
                {
                    step++;
                    Vector2 centre = a + dir * t;
                    bool pair = Hash01(seed, step, 0) < style.PathPairChance;
                    int count = pair ? 2 : 1;
                    for (int k = 0; k < count; k++)
                    {
                        float side = pair ? (k == 0 ? -style.PathHalfWidth : style.PathHalfWidth) : 0f;
                        side += (Hash01(seed, step, 1 + k) - 0.5f) * 2f * style.PathJitter;
                        float along = (Hash01(seed, step, 3 + k) - 0.5f) * 2f * style.PathJitter;
                        float radius = Mathf.Lerp(style.StoneRadius.x, style.StoneRadius.y, Hash01(seed, step, 5 + k));
                        if (pair) radius *= 0.85f;
                        Vector2 pos = centre + normal * side + dir * along;
                        var item = new FloorDecorItem(FloorDecorKind.Stone, pos, radius,
                            Mathf.Lerp(style.StoneAspect.x, style.StoneAspect.y, Hash01(seed, step, 7 + k)),
                            Hash01(seed, step, 9 + k) * 360f, (int)(Hash(seed, step, 11 + k) & 0xFFFF));
                        if (triangles + 2 * tri > budget) return triangles;
                        if (TryAddWithTwin(in grid, in style, item, radius + style.ClearMargin, radius * 1.5f, items)) triangles += 2 * tri;
                    }
                }
                carry = t - len;
            }
            return triangles;
        }

        private static int Scatter(in TileGrid grid, in EnvDecorStyle style, uint seed, List<FloorDecorItem> items, int triangles, int budget)
        {
            int cells = grid.Width * grid.Height;
            float clearance = style.StoneClearance * style.StoneClearance;
            for (int i = 0; i < cells; i++)
            {
                int mirror = cells - 1 - i;
                if (i >= mirror) break;
                int c = i % grid.Width, r = i / grid.Width;
                if (grid.Tiles[i] != TileType.Floor || grid.Tiles[mirror] != TileType.Floor) continue;
                float u = Hash01(seed, i, 101);
                FloorDecorKind kind;
                if (u < style.PebbleChance) kind = FloorDecorKind.Pebble;
                else if (u < style.PebbleChance + style.SproutChance) kind = FloorDecorKind.Sprout;
                else if (u < style.PebbleChance + style.SproutChance + style.FlowerChance) kind = FloorDecorKind.Flower;
                else continue;
                var pos = new Vector2(c + 0.5f + (Hash01(seed, i, 102) - 0.5f) * 0.6f, r + 0.5f + (Hash01(seed, i, 103) - 0.5f) * 0.6f);
                bool nearStone = false;
                for (int k = 0; k < items.Count && !nearStone; k++)
                    nearStone = items[k].Kind == FloorDecorKind.Stone && (items[k].Position - pos).sqrMagnitude < clearance;
                if (nearStone) continue;
                float size;
                int variant = (int)(Hash(seed, i, 104) & 0xFFFF);
                switch (kind)
                {
                    case FloorDecorKind.Pebble: size = Mathf.Lerp(style.PebbleRadius.x, style.PebbleRadius.y, Hash01(seed, i, 105)); break;
                    case FloorDecorKind.Sprout: size = style.SproutSize * Mathf.Lerp(0.8f, 1.15f, Hash01(seed, i, 105)); break;
                    default:
                        size = style.FlowerSize * Mathf.Lerp(0.85f, 1.15f, Hash01(seed, i, 105));
                        int colours = style.FlowerPetals != null && style.FlowerPetals.Length > 0 ? style.FlowerPetals.Length : 1;
                        variant = (int)(Hash(seed, i, 106) % (uint)colours);
                        break;
                }
                int tri = FloorDecorMeshBuilder.TrianglesOf(kind);
                if (triangles + 2 * tri > budget) break;
                var item = new FloorDecorItem(kind, pos, size, Mathf.Lerp(0.75f, 1f, Hash01(seed, i, 107)), Hash01(seed, i, 108) * 360f, variant);
                if (TryAddWithTwin(in grid, in style, item, size + 0.04f, 0f, items)) triangles += 2 * tri;
            }
            return triangles;
        }

        /// <summary>Adds the item and its 180° twin if both lie on free floor with <paramref name="margin"/> around them.</summary>
        private static bool TryAddWithTwin(in TileGrid grid, in EnvDecorStyle style, FloorDecorItem item, float margin, float spacing,
            List<FloorDecorItem> items)
        {
            Vector2 twinPos = Mirror(item.Position, grid.Width, grid.Height);
            if (!IsFree(in grid, item.Position, margin) || !IsFree(in grid, twinPos, margin)) return false;
            if (spacing > 0f)
            {
                float sq = spacing * spacing;
                for (int k = 0; k < items.Count; k++)
                {
                    if (items[k].Kind != FloorDecorKind.Stone) continue;
                    if ((items[k].Position - item.Position).sqrMagnitude < sq) return false;
                }
                // The twin of a stone right at the centre would overlap it.
                if ((twinPos - item.Position).sqrMagnitude < sq) return false;
            }
            items.Add(item);
            FloorDecorItem twin = item;
            twin.Position = twinPos;
            twin.YawDegrees = item.YawDegrees + 180f;
            items.Add(twin);
            return true;
        }

        /// <summary>True if the point and the four points <paramref name="margin"/> away along the axes are on open floor.</summary>
        public static bool IsFree(in TileGrid grid, Vector2 p, float margin)
        {
            return IsFloor(in grid, p.x, p.y) && IsFloor(in grid, p.x - margin, p.y) && IsFloor(in grid, p.x + margin, p.y) &&
                   IsFloor(in grid, p.x, p.y - margin) && IsFloor(in grid, p.x, p.y + margin);
        }

        private static bool IsFloor(in TileGrid grid, float x, float y)
        {
            int c = Mathf.FloorToInt(x), r = Mathf.FloorToInt(y);
            return grid.InBounds(c, r) && grid.Get(c, r) == TileType.Floor;
        }

        /// <summary>The floor cell nearest to a point given as map fractions.</summary>
        private static bool NearestFree(in TileGrid grid, float fx, float fy, out GridCell cell)
        {
            var target = new Vector2(fx * grid.Width, fy * grid.Height);
            float best = float.MaxValue;
            cell = default;
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != TileType.Floor) continue;
                    float d = (new Vector2(c + 0.5f, r + 0.5f) - target).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    cell = new GridCell(c, r);
                }
            }
            return best < float.MaxValue;
        }

        private static Vector2 Centre(GridCell cell) => new Vector2(cell.Column + 0.5f, cell.Row + 0.5f);

        /// <summary>Chaikin corner cutting (keeps the end points): the A* zigzag becomes a soft curve.</summary>
        private static void Smooth(List<Vector2> line)
        {
            for (int pass = 0; pass < SmoothingPasses && line.Count > 2; pass++)
            {
                var result = new List<Vector2>(line.Count * 2) { line[0] };
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    result.Add(Vector2.Lerp(line[i], line[i + 1], 0.25f));
                    result.Add(Vector2.Lerp(line[i], line[i + 1], 0.75f));
                }
                result.Add(line[line.Count - 1]);
                line.Clear();
                line.AddRange(result);
            }
        }

        private static uint Seed(in TileGrid grid)
        {
            unchecked
            {
                uint h = 2166136261u ^ (uint)grid.Width * 31u ^ (uint)grid.Height;
                for (int i = 0; i < grid.Tiles.Length; i++) h = (h ^ (uint)grid.Tiles[i]) * 16777619u;
                return h;
            }
        }

        private static uint Hash(uint seed, int a, int b)
        {
            unchecked
            {
                uint h = seed ^ (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }

        private static float Hash01(uint seed, int a, int b) => (Hash(seed, a, b) & 0xFFFFFF) / 16777216f;
    }
}
