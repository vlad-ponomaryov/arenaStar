using System.Collections.Generic;
using Arena.Core.Map;
using Arena.Data;
using Arena.Data.Conversion;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// D-142 (Snow Valley feedback 1, "пусто"): footprint trails on the snow from every spawn (shared or team, A3.map) towards the map centre, view only. A
    /// trail follows the shortest 8-neighbour floor path (no corner cutting, around bushes and water) to the cells within
    /// <see cref="MapThemeConfig.TrailStopCells"/> of the centre, is smoothed (Chaikin) and shifted sideways by a hash, then gets
    /// alternating left / right oval prints every <see cref="MapThemeConfig.TrailStep"/>. One flat mesh (vertex colour, uv0.x = 1 for
    /// Arena/ToonEnv) just above the floor; built once at match start.
    /// </summary>
    public static class SnowTrailBuilder
    {
        private const float PrintHeight = 0.006f;
        private const int PrintSides = 6;
        private const int SmoothPasses = 3;
        private const float Diagonal = 1.41421356f;

        /// <summary>The trail mesh of the map, or null (no trails in the theme, no spawns or no path).</summary>
        public static Mesh Build(in TileGrid grid, string[] rows, MapThemeConfig theme)
        {
            if (theme == null || !theme.Trails || rows == null) return null;
            float[] dist = DistanceToCentre(in grid, theme.TrailStopCells);
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            Color color = theme.TrailColor.linear;
            color.a = 0f;
            var path = new List<Vector2>();
            for (int t = 0; t < rows.Length; t++)
            {
                string line = rows[t];
                for (int c = 0; line != null && c < line.Length; c++)
                {
                    char ch = line[c];
                    if (ch != MapConverter.SharedSpawnChar && ch != MapConverter.Team0SpawnChar && ch != MapConverter.Team1SpawnChar) continue;
                    int r = grid.Height - 1 - t;
                    if (!Trace(in grid, dist, c, r, path)) continue;
                    for (int k = 0; k < SmoothPasses; k++) Chaikin(path);
                    float spread = ((MapView.CellHash(c, r) & 0xFF) / 255f - 0.5f) * 2f * theme.TrailSpread;
                    AddPrints(path, spread, theme, color, verts, colors, tris);
                }
            }
            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = "SnowTrails" };
            mesh.indexFormat = verts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            var normals = new List<Vector3>(verts.Count);
            var uvs = new List<Vector2>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(1f, 0f));
            }
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool Walk(in TileGrid grid, int c, int r) => grid.InBounds(c, r) && grid.Get(c, r) == TileType.Floor;

        /// <summary>Path length over floor cells (8 neighbours, no corner cutting) to the floor cells near the centre; ∞ elsewhere.</summary>
        private static float[] DistanceToCentre(in TileGrid grid, int stopCells)
        {
            int w = grid.Width, h = grid.Height;
            var dist = new float[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.PositiveInfinity;
            int cc = (w - 1) / 2, cr = (h - 1) / 2;
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    if (Walk(in grid, c, r) && Mathf.Max(Mathf.Abs(c - cc), Mathf.Abs(r - cr)) <= stopCells) dist[r * w + c] = 0f;
            // Relaxation sweeps (forward and backward) until nothing changes: a few dozen passes on a 45 × 45 map, once per match.
            bool changed = true;
            for (int pass = 0; changed && pass < w * h; pass++)
            {
                changed = false;
                for (int k = 0; k < w * h; k++)
                {
                    int i = (pass & 1) == 0 ? k : w * h - 1 - k;
                    int c = i % w, r = i / w;
                    if (!Walk(in grid, c, r)) continue;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if ((dc == 0 && dr == 0) || !Walk(in grid, c + dc, r + dr)) continue;
                            if (dc != 0 && dr != 0 && (!Walk(in grid, c + dc, r) || !Walk(in grid, c, r + dr))) continue;
                            float d = dist[(r + dr) * w + c + dc] + (dc != 0 && dr != 0 ? Diagonal : 1f);
                            if (d < dist[i] - 1e-4f)
                            {
                                dist[i] = d;
                                changed = true;
                            }
                        }
                    }
                }
            }
            return dist;
        }

        /// <summary>Cell centres from (<paramref name="c"/>, <paramref name="r"/>) down the distance field to the centre area.</summary>
        private static bool Trace(in TileGrid grid, float[] dist, int c, int r, List<Vector2> path)
        {
            path.Clear();
            int w = grid.Width;
            if (!Walk(in grid, c, r) || float.IsInfinity(dist[r * w + c])) return false;
            path.Add(new Vector2(c + 0.5f, r + 0.5f));
            for (int guard = 0; dist[r * w + c] > 0f && guard < dist.Length; guard++)
            {
                int bc = c, br = r;
                float best = dist[r * w + c];
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if ((dc == 0 && dr == 0) || !Walk(in grid, c + dc, r + dr)) continue;
                        if (dc != 0 && dr != 0 && (!Walk(in grid, c + dc, r) || !Walk(in grid, c, r + dr))) continue;
                        float d = dist[(r + dr) * w + c + dc];
                        if (d < best) { best = d; bc = c + dc; br = r + dr; }
                    }
                if (bc == c && br == r) break;
                c = bc;
                r = br;
                path.Add(new Vector2(c + 0.5f, r + 0.5f));
            }
            return path.Count > 2;
        }

        private static void Chaikin(List<Vector2> path)
        {
            if (path.Count < 3) return;
            var result = new List<Vector2>(path.Count * 2) { path[0] };
            for (int i = 0; i + 1 < path.Count; i++)
            {
                result.Add(Vector2.Lerp(path[i], path[i + 1], 0.25f));
                result.Add(Vector2.Lerp(path[i], path[i + 1], 0.75f));
            }
            result.Add(path[path.Count - 1]);
            path.Clear();
            path.AddRange(result);
        }

        private static void AddPrints(List<Vector2> path, float spread, MapThemeConfig theme, Color color, List<Vector3> verts, List<Color> colors,
            List<int> tris)
        {
            float next = theme.TrailStep * 0.5f, run = 0f;
            int side = 0;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                float length = Vector2.Distance(a, b);
                if (length < 1e-5f) continue;
                Vector2 along = (b - a) / length;
                var across = new Vector2(-along.y, along.x);
                while (next <= run + length)
                {
                    Vector2 p = a + along * (next - run) + across * (spread + (side == 0 ? -1f : 1f) * theme.TrailSideOffset);
                    AddPrint(p, along, across, theme.TrailPrintSize, color, verts, colors, tris);
                    side ^= 1;
                    next += theme.TrailStep;
                }
                run += length;
            }
        }

        private static void AddPrint(Vector2 p, Vector2 along, Vector2 across, Vector2 size, Color color, List<Vector3> verts, List<Color> colors,
            List<int> tris)
        {
            int centre = verts.Count;
            verts.Add(new Vector3(p.x, PrintHeight, p.y));
            colors.Add(color);
            for (int k = 0; k < PrintSides; k++)
            {
                float a = k * Mathf.PI * 2f / PrintSides;
                Vector2 q = p + across * (Mathf.Cos(a) * size.x) + along * (Mathf.Sin(a) * size.y);
                verts.Add(new Vector3(q.x, PrintHeight, q.y));
                colors.Add(color);
            }
            for (int k = 0; k < PrintSides; k++)
            {
                int k1 = (k + 1) % PrintSides;
                // The ring runs clockwise seen from above (across → along), Unity's front face: up.
                tris.Add(centre);
                tris.Add(centre + 1 + k);
                tris.Add(centre + 1 + k1);
            }
        }
    }
}
