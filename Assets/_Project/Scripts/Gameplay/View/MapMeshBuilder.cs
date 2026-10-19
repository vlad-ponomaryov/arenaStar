using System.Collections.Generic;
using Arena.Core.Map;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Builds combined meshes of the static map parts at match start (one mesh per material, D-014; Art-Spec §2.1, A.3a):
    /// walls as chamfered blocks with top/side vertex colours, the floor as a grid of cells with soft occlusion near walls, the
    /// meadow frame around the arena, water cells as one flat sheet. Every mesh has vertex colours (linear; alpha = gloss 0) —
    /// meshes under <c>Arena/*</c> shaders need them. Coordinates per D-009: cell (c, r) spans world x ∈ [c, c+1], z ∈ [r, r+1].
    /// Allocates only here (load time), never per frame.
    /// </summary>
    public static class MapMeshBuilder
    {
        /// <summary>
        /// Blocks of height <paramref name="height"/> over every cell of <paramref name="type"/>. The upper edges facing a
        /// non-wall cell (or the map edge) are chamfered by <see cref="MapMeshStyle.WallBevel"/>, mitred at outer and inner corners;
        /// a top is split into 3×3 parts (inset, edge strips, corners) so neighbouring blocks share their edges without gaps.
        /// Sides are built only where the neighbour is not a wall; the map edge counts as open (the meadow is seen around the arena).
        /// </summary>
        public static Mesh BuildBlocks(in TileGrid grid, TileType type, float height, in MapMeshStyle style, string name)
        {
            var b = new Buffers();
            float bevel = Mathf.Clamp(style.WallBevel, 0f, height * 0.5f);
            float low = height - bevel;
            var xs = new float[4];
            var hs = new float[16];
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != type) continue;
                    float tint = 1f + CellNoise(c, r) * style.WallCellVariation;
                    Color topSrgb = Scale(style.WallTop, tint);
                    Color top = topSrgb.linear;
                    Color rim = Color.Lerp(topSrgb, Color.white, style.WallBevelHighlight).linear;
                    Color side = Scale(style.WallSide, tint).linear;
                    Color foot = Scale(style.WallSide, tint * style.WallFootShade).linear;

                    bool w = IsType(in grid, c - 1, r, type), e = IsType(in grid, c + 1, r, type);
                    bool s = IsType(in grid, c, r - 1, type), n = IsType(in grid, c, r + 1, type);
                    xs[0] = 0f; xs[1] = bevel; xs[2] = 1f - bevel; xs[3] = 1f;
                    for (int j = 0; j < 4; j++)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            bool edgeX = i == 0 || i == 3, edgeZ = j == 0 || j == 3;
                            bool high;
                            if (!edgeX && !edgeZ) high = true;
                            else if (edgeX && !edgeZ) high = i == 0 ? w : e;
                            else if (!edgeX) high = j == 0 ? s : n;
                            else
                            {
                                int dx = i == 0 ? -1 : 1, dz = j == 0 ? -1 : 1;
                                high = (dx < 0 ? w : e) && (dz < 0 ? s : n) && IsType(in grid, c + dx, r + dz, type);
                            }
                            hs[j * 4 + i] = high ? height : low;
                        }
                    }

                    for (int j = 0; j < 3; j++)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            if (xs[i + 1] - xs[i] <= 0f || xs[j + 1] - xs[j] <= 0f) continue;
                            var p00 = new Vector3(c + xs[i], hs[j * 4 + i], r + xs[j]);
                            var p10 = new Vector3(c + xs[i + 1], hs[j * 4 + i + 1], r + xs[j]);
                            var p11 = new Vector3(c + xs[i + 1], hs[(j + 1) * 4 + i + 1], r + xs[j + 1]);
                            var p01 = new Vector3(c + xs[i], hs[(j + 1) * 4 + i], r + xs[j + 1]);
                            // Corner parts split along the diagonal from the inner point to the cell corner (the mitre).
                            bool antiDiagonal = (i == 2 && j == 0) || (i == 0 && j == 2);
                            if (antiDiagonal)
                            {
                                AddTop(b, p00, p10, p01, height, top, rim);
                                AddTop(b, p10, p11, p01, height, top, rim);
                            }
                            else
                            {
                                AddTop(b, p00, p10, p11, height, top, rim);
                                AddTop(b, p00, p11, p01, height, top, rim);
                            }
                        }
                    }

                    float x0 = c, x1 = c + 1f, z0 = r, z1 = r + 1f;
                    if (!s) b.AddSide(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), low, Vector3.back, side, foot);
                    if (!n) b.AddSide(new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), low, Vector3.forward, side, foot);
                    if (!w) b.AddSide(new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), low, Vector3.left, side, foot);
                    if (!e) b.AddSide(new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), low, Vector3.right, side, foot);
                }
            }
            return b.Create(name);
        }

        /// <summary>
        /// Floor of the arena: a grid of <see cref="MapMeshStyle.FloorSubdivisions"/>² quads per cell over every cell except
        /// <paramref name="covered"/> (walls stand there), at height 0, normals up. Vertex colour darkens towards
        /// <see cref="MapMeshStyle.FloorOcclusionColor"/> near covered cells (soft contact shade, D-110: occlusion in vertices).
        /// </summary>
        public static Mesh BuildFloor(in TileGrid grid, TileType covered, in MapMeshStyle style, string name)
        {
            int sub = Mathf.Max(1, style.FloorSubdivisions);
            int vw = grid.Width * sub + 1, vh = grid.Height * sub + 1;
            float step = 1f / sub;
            float radius = Mathf.Max(0.01f, style.FloorOcclusionRadius);
            int reach = Mathf.CeilToInt(radius) + 1;
            Color occluded = style.FloorOcclusionColor.linear;

            var verts = new List<Vector3>(vw * vh);
            var colors = new List<Color>(vw * vh);
            for (int j = 0; j < vh; j++)
            {
                for (int i = 0; i < vw; i++)
                {
                    float x = i * step, z = j * step;
                    float d = DistanceToType(in grid, x, z, covered, reach);
                    float t = 1f - Mathf.Clamp01(d / radius);
                    float occlusion = style.FloorWallOcclusion * t * t * (3f - 2f * t);
                    Color col = Color.Lerp(Color.white, occluded, occlusion);
                    col.a = 0f;
                    verts.Add(new Vector3(x, 0f, z));
                    colors.Add(col);
                }
            }

            var tris = new List<int>(grid.Width * grid.Height * sub * sub * 6);
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) == covered) continue;
                    for (int sj = 0; sj < sub; sj++)
                    {
                        for (int si = 0; si < sub; si++)
                        {
                            int i = c * sub + si, j = r * sub + sj;
                            int v00 = j * vw + i, v10 = v00 + 1, v01 = v00 + vw, v11 = v01 + 1;
                            // Clockwise seen from above (Unity front face up).
                            tris.Add(v00); tris.Add(v01); tris.Add(v11);
                            tris.Add(v00); tris.Add(v11); tris.Add(v10);
                        }
                    }
                }
            }

            var normals = new List<Vector3>(verts.Count);
            for (int k = 0; k < verts.Count; k++) normals.Add(Vector3.up);
            return Create(name, verts, normals, colors, tris);
        }

        /// <summary>
        /// Meadow frame around a <paramref name="width"/> × <paramref name="height"/> arena (Art-Spec §2.1): from the map edge out
        /// to <see cref="MapMeshStyle.MeadowMargin"/>, at height 0. Vertex colours: <see cref="MapMeshStyle.MeadowInner"/> at the
        /// arena edge → white after <see cref="MapMeshStyle.MeadowInnerWidth"/> → white until the fade → <see cref="MapMeshStyle.MeadowEdge"/>
        /// at the outer edge (blends into the camera background).
        /// </summary>
        public static Mesh BuildMeadow(int width, int height, in MapMeshStyle style, string name)
        {
            float margin = Mathf.Max(0f, style.MeadowMargin);
            var b = new Buffers();
            if (margin <= 0f) return b.Create(name);
            float inner = Mathf.Clamp(style.MeadowInnerWidth, 0f, margin);
            float fade = Mathf.Clamp(style.MeadowFade, 0f, margin - inner);
            float[] rings = { 0f, inner, margin - fade, margin };
            Color innerCol = Matte(style.MeadowInner.linear), edgeCol = Matte(style.MeadowEdge.linear), white = new Color(1f, 1f, 1f, 0f);
            Color[] cols = { innerCol, white, white, edgeCol };
            for (int k = 0; k < 3; k++)
            {
                float a = rings[k], o = rings[k + 1];
                if (o - a <= 0f) continue;
                // Inner rectangle [−a, W+a] × [−a, H+a], outer [−o, W+o] × [−o, H+o]; four trapezoids.
                var i00 = new Vector3(-a, 0f, -a); var i10 = new Vector3(width + a, 0f, -a);
                var i11 = new Vector3(width + a, 0f, height + a); var i01 = new Vector3(-a, 0f, height + a);
                var o00 = new Vector3(-o, 0f, -o); var o10 = new Vector3(width + o, 0f, -o);
                var o11 = new Vector3(width + o, 0f, height + o); var o01 = new Vector3(-o, 0f, height + o);
                Color ci = cols[k], co = cols[k + 1];
                b.AddQuad(o00, co, i00, ci, i10, ci, o10, co, Vector3.up); // south
                b.AddQuad(i10, ci, i11, ci, o11, co, o10, co, Vector3.up); // east
                b.AddQuad(i11, ci, i01, ci, o01, co, o11, co, Vector3.up); // north
                b.AddQuad(i01, ci, i00, ci, o00, co, o01, co, Vector3.up); // west
            }
            return b.Create(name);
        }

        /// <summary>One upward quad per cell of <paramref name="type"/> at <paramref name="height"/> (water sheet), white vertices.</summary>
        public static Mesh BuildSheet(in TileGrid grid, TileType type, float height, string name)
        {
            var b = new Buffers();
            var white = new Color(1f, 1f, 1f, 0f);
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != type) continue;
                    float x0 = c, x1 = c + 1f, z0 = r, z1 = r + 1f;
                    b.AddTriangle(new Vector3(x0, height, z0), new Vector3(x0, height, z1), new Vector3(x1, height, z1), Vector3.up, white, white, white);
                    b.AddTriangle(new Vector3(x0, height, z0), new Vector3(x1, height, z1), new Vector3(x1, height, z0), Vector3.up, white, white, white);
                }
            }
            return b.Create(name);
        }

        /// <summary>Distance in the XZ plane from (x, z) to the nearest cell of <paramref name="type"/> within <paramref name="reach"/> cells.</summary>
        private static float DistanceToType(in TileGrid grid, float x, float z, TileType type, int reach)
        {
            int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
            float best = float.MaxValue;
            for (int r = cz - reach; r <= cz + reach; r++)
            {
                for (int c = cx - reach; c <= cx + reach; c++)
                {
                    if (!IsType(in grid, c, r, type)) continue;
                    float dx = Mathf.Max(c - x, 0f, x - (c + 1f));
                    float dz = Mathf.Max(r - z, 0f, z - (r + 1f));
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>Upper triangle of a block: flat at full height → top colour, otherwise part of the chamfer → rim colour.</summary>
        private static void AddTop(Buffers b, Vector3 p0, Vector3 p1, Vector3 p2, float height, Color top, Color rim)
        {
            bool flat = Mathf.Abs(p0.y - height) < 1e-5f && Mathf.Abs(p1.y - height) < 1e-5f && Mathf.Abs(p2.y - height) < 1e-5f;
            Color c = flat ? top : rim;
            b.AddTriangle(p0, p1, p2, Vector3.up, c, c, c);
        }

        private static bool IsType(in TileGrid grid, int c, int r, TileType type) => grid.InBounds(c, r) && grid.Get(c, r) == type;

        /// <summary>Deterministic value in [−1, 1] per cell (view variation only, not the simulation RNG).</summary>
        private static float CellNoise(int c, int r)
        {
            unchecked
            {
                uint h = (uint)(c * 73856093) ^ (uint)(r * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFF) / 32767.5f - 1f;
            }
        }

        private static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static Color Matte(Color c) => new Color(c.r, c.g, c.b, 0f);

        private static Mesh Create(string name, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flat-shaded geometry: every triangle/quad gets its own vertices and face normal.</summary>
        private sealed class Buffers
        {
            private readonly List<Vector3> _verts = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<int> _tris = new List<int>();

            /// <summary>Triangle facing the side of <paramref name="towards"/> (winding fixed if needed), flat normal.</summary>
            public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 towards, Color ca, Color cb, Color cc)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(n, towards) < 0f)
                {
                    (b, c) = (c, b);
                    (cb, cc) = (cc, cb);
                    n = -n;
                }
                n.Normalize();
                int i = _verts.Count;
                _verts.Add(a); _verts.Add(b); _verts.Add(c);
                _normals.Add(n); _normals.Add(n); _normals.Add(n);
                _colors.Add(WithGloss(ca)); _colors.Add(WithGloss(cb)); _colors.Add(WithGloss(cc));
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            }

            /// <summary>Convex planar quad a-b-c-d (cyclic order, any winding) facing <paramref name="normal"/>, colour per corner.</summary>
            public void AddQuad(Vector3 a, Color ca, Vector3 b, Color cb, Vector3 c, Color cc, Vector3 d, Color cd, Vector3 normal)
            {
                AddTriangle(a, b, c, normal, ca, cb, cc);
                AddTriangle(a, c, d, normal, ca, cc, cd);
            }

            /// <summary>Vertical side from <paramref name="a"/> to <paramref name="b"/> on the ground up to <paramref name="top"/>.</summary>
            public void AddSide(Vector3 a, Vector3 b, float top, Vector3 outward, Color upper, Color foot)
            {
                var at = new Vector3(a.x, top, a.z);
                var bt = new Vector3(b.x, top, b.z);
                AddTriangle(a, at, bt, outward, foot, upper, upper);
                AddTriangle(a, bt, b, outward, foot, upper, foot);
            }

            public Mesh Create(string name) => MapMeshBuilder.Create(name, _verts, _normals, _colors, _tris);

            private static Color WithGloss(Color c) => new Color(c.r, c.g, c.b, 0f);
        }
    }
}
