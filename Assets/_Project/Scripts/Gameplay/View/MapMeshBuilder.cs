using System.Collections.Generic;
using Arena.Core.Map;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Builds combined meshes of the static map parts at match start (one mesh per material, D-014; Art-Spec §2.1, A.3a):
    /// walls as chamfered blocks with top/side vertex colours, the floor as a grid of cells with soft occlusion near walls, the
    /// meadow frame around the arena, water cells as one sunken sheet with a shore factor and its banks (A.3b). Every mesh has vertex colours (linear; alpha = gloss 0) —
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
        public static Mesh BuildBlocks(in TileGrid grid, TileType type, float height, in MapMeshStyle style, string name) =>
            BuildBlocks(in grid, type, height, in style, name, null);

        /// <summary>
        /// Blocks as above, without the cells where <paramref name="skip"/> is true (A.3c: wall groups drawn as props); skipped cells
        /// still count as walls for their neighbours' chamfers and sides.
        /// </summary>
        public static Mesh BuildBlocks(in TileGrid grid, TileType type, float height, in MapMeshStyle style, string name, bool[] skip)
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
                    if (skip != null && skip[grid.IndexOf(c, r)]) continue;
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
        public static Mesh BuildFloor(in TileGrid grid, TileType covered, in MapMeshStyle style, string name) =>
            BuildFloor(in grid, covered, covered, in style, name);

        /// <summary>
        /// The floor as above, also without cells of <paramref name="hole"/> (A.3b: the sunken pond — the water sheet lies lower and
        /// <see cref="BuildBanks"/> closes its sides). Occlusion comes only from <paramref name="covered"/>.
        /// </summary>
        public static Mesh BuildFloor(in TileGrid grid, TileType covered, TileType hole, in MapMeshStyle style, string name) =>
            BuildFloor(in grid, covered, hole, in style, name, null);

        /// <summary>
        /// The floor as above; cells where <paramref name="props"/> is true (A.3c: walls drawn as round props) get floor too, with a
        /// round contact shade of <see cref="MapMeshStyle.ObstaclePropRadius"/> instead of the square wall occlusion.
        /// </summary>
        public static Mesh BuildFloor(in TileGrid grid, TileType covered, TileType hole, in MapMeshStyle style, string name, bool[] props)
        {
            int sub = Mathf.Max(1, style.FloorSubdivisions);
            int vw = grid.Width * sub + 1, vh = grid.Height * sub + 1;
            float step = 1f / sub;
            float radius = Mathf.Max(0.01f, style.FloorOcclusionRadius);
            int reach = Mathf.CeilToInt(radius) + 1;
            Color occluded = style.FloorOcclusionColor.linear;
            float bushRadius = Mathf.Max(0.01f, style.FloorBushOcclusionRadius);
            int bushReach = Mathf.CeilToInt(bushRadius) + 1;
            float patchCell = Mathf.Max(0.1f, style.FloorPatchCell);

            var verts = new List<Vector3>(vw * vh);
            var colors = new List<Color>(vw * vh);
            var uvs = new List<Vector2>(vw * vh);
            for (int j = 0; j < vh; j++)
            {
                for (int i = 0; i < vw; i++)
                {
                    float x = i * step, z = j * step;
                    float d = Mathf.Min(DistanceToType(in grid, x, z, covered, reach, props), DistanceToProp(in grid, x, z, props, style.ObstaclePropRadius));
                    float t = 1f - Mathf.Clamp01(d / radius);
                    float occlusion = style.FloorWallOcclusion * t * t * (3f - 2f * t);
                    // A.3c: contact shade and a grass fringe around tall-grass bushes.
                    float db = DistanceToType(in grid, x, z, TileType.Bush, bushReach);
                    float tb = 1f - Mathf.Clamp01(db / bushRadius);
                    float nearBush = tb * tb * (3f - 2f * tb);
                    occlusion = Mathf.Max(occlusion, style.FloorBushOcclusion * nearBush);
                    Color col = Color.Lerp(Color.white, occluded, occlusion);
                    col.a = 0f;
                    verts.Add(new Vector3(x, 0f, z));
                    colors.Add(col);
                    // Patch weight: value noise thresholded around 1 − coverage (0.5 = the patch edge in the shader).
                    float noise = ValueNoise(x / patchCell, z / patchCell);
                    float patch = Mathf.Clamp01(0.5f + (noise - (1f - style.FloorPatchCoverage)) * PatchNoiseGain);
                    if (style.FloorPatchCoverage <= 0f) patch = 0f;
                    patch = Mathf.Max(patch, style.FloorPatchNearBush * nearBush);
                    uvs.Add(new Vector2(0f, patch));
                }
            }

            var tris = new List<int>(grid.Width * grid.Height * sub * sub * 6);
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    TileType tile = grid.Get(c, r);
                    bool prop = props != null && props[grid.IndexOf(c, r)];
                    if ((tile == covered && !prop) || tile == hole) continue;
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
            Mesh mesh = Create(name, verts, normals, colors, tris);
            mesh.SetUVs(0, uvs);
            return mesh;
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

        /// <summary>
        /// Water sheet (A.3b): <see cref="MapMeshStyle.WaterSubdivisions"/>² upward quads per cell of <paramref name="type"/> at
        /// <paramref name="height"/>. Vertex rgb white; vertex alpha = shore factor for <c>Arena/Water</c>: 1 at a cell that is not
        /// <paramref name="type"/> (or the map edge), fading smoothly to 0 at <see cref="MapMeshStyle.WaterShoreWidth"/>.
        /// </summary>
        public static Mesh BuildSheet(in TileGrid grid, TileType type, float height, in MapMeshStyle style, string name)
        {
            int sub = Mathf.Max(1, style.WaterSubdivisions);
            float step = 1f / sub;
            float width = Mathf.Max(0.01f, style.WaterShoreWidth);
            int reach = Mathf.CeilToInt(width) + 1;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != type) continue;
                    int first = verts.Count;
                    for (int j = 0; j <= sub; j++)
                    {
                        for (int i = 0; i <= sub; i++)
                        {
                            float x = c + i * step, z = r + j * step;
                            float d = DistanceToOther(in grid, x, z, type, reach);
                            float t = 1f - Mathf.Clamp01(d / width);
                            verts.Add(new Vector3(x, height, z));
                            colors.Add(new Color(1f, 1f, 1f, t * t * (3f - 2f * t)));
                        }
                    }
                    for (int j = 0; j < sub; j++)
                    {
                        for (int i = 0; i < sub; i++)
                        {
                            int v00 = first + j * (sub + 1) + i, v10 = v00 + 1, v01 = v00 + sub + 1, v11 = v01 + 1;
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
        /// Banks of a sunken pond (A.3b): along every side of a <paramref name="type"/> cell whose neighbour is not
        /// <paramref name="type"/> (or the map edge), a vertical face from the ground (y = 0) down to <paramref name="bottom"/>, facing
        /// into the water. Colours <see cref="MapMeshStyle.BankTop"/> → <see cref="MapMeshStyle.BankFoot"/> (linear, matte); drawn with
        /// the wall material (planks on side faces). Empty if <paramref name="bottom"/> ≥ 0 (water on top of the floor).
        /// </summary>
        public static Mesh BuildBanks(in TileGrid grid, TileType type, float bottom, in MapMeshStyle style, string name)
        {
            var b = new Buffers();
            if (bottom >= 0f) return b.Create(name);
            Color top = style.BankTop.linear, foot = style.BankFoot.linear;
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != type) continue;
                    float x0 = c, x1 = c + 1f, z0 = r, z1 = r + 1f;
                    if (!IsType(in grid, c, r - 1, type)) b.AddBank(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), bottom, Vector3.forward, top, foot);
                    if (!IsType(in grid, c, r + 1, type)) b.AddBank(new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), bottom, Vector3.back, top, foot);
                    if (!IsType(in grid, c - 1, r, type)) b.AddBank(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), bottom, Vector3.right, top, foot);
                    if (!IsType(in grid, c + 1, r, type)) b.AddBank(new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), bottom, Vector3.left, top, foot);
                }
            }
            return b.Create(name);
        }

        /// <summary>
        /// Round pond (A.3f): the water cells of <paramref name="type"/> drawn as one organic pond with a plank rim instead of square
        /// sheets. The shore is smoothed by the neighbour mask of each cell corner: a convex corner (both side neighbours are land)
        /// becomes an arc of up to <see cref="MapMeshStyle.PondCornerRadius"/> (a single cell → a disc, 2 × 2 → a circle), a concave
        /// corner gets a small notch so the rim runs on. The rim (<see cref="MapMeshStyle.PondRimWidth"/>, planks across it) lies
        /// inside the water cells, so walkable floor is never covered; the sand left outside an arc is filled by
        /// <paramref name="fill"/> (floor material). View only: the cells still are water for the simulation.
        /// Returns the water surface (vertex alpha = shore factor for <c>Arena/Water</c>, capped at <see cref="MapMeshStyle.PondShoreMax"/>);
        /// <paramref name="rim"/> = planks + sides (vertex colours, matte), <paramref name="fill"/> = floor pieces at the arcs.
        /// </summary>
        public static Mesh BuildPond(in TileGrid grid, TileType type, float waterHeight, float bankBottom, in MapMeshStyle style,
            string name, out Mesh rim, out Mesh fill)
        {
            float w = Mathf.Max(0.01f, style.PondRimWidth), top = style.PondRimHeight;
            float shoreWidth = Mathf.Max(0.01f, style.WaterShoreWidth);
            int reach = Mathf.CeilToInt(shoreWidth + w) + 1;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            var rimBuf = new Buffers();
            var fillBuf = new Buffers();
            var outer = new List<Vector2>();
            var inner = new List<Vector2>();
            var isRim = new List<bool>();
            var contour = new List<Vector2>();
            Color light = style.PondPlankLight.linear, dark = style.PondPlankDark.linear, seam = style.PondPlankSeam.linear;
            Color side = style.PondRimSide.linear, sideFoot = Scale(side, 0.55f), sand = new Color(1f, 1f, 1f, 0f);
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != type) continue;
                    outer.Clear(); inner.Clear(); isRim.Clear();
                    var origin = new Vector2(c, r);
                    for (int k = 0; k < 4; k++)
                    {
                        int prev = (k + 3) & 3;
                        bool landIn = IsPondLand(in grid, c, r, prev, type), landOut = IsPondLand(in grid, c, r, k, type);
                        Vector2 corner = origin + PondCorner[k];
                        Vector2 dirIn = PondEdgeDir[prev], dirOut = PondEdgeDir[k];
                        if (landIn && landOut)
                        {
                            bool otherIn = IsPondLand(in grid, c, r, (k + 2) & 3, type);   // edge prev's far corner is convex too
                            bool otherOut = IsPondLand(in grid, c, r, (k + 1) & 3, type);
                            float radius = Mathf.Min(style.PondCornerRadius, otherIn ? 0.5f : 1f, otherOut ? 0.5f : 1f);
                            radius = Mathf.Max(radius, w + 0.02f);
                            Vector2 inPrev = PondInward[prev], inK = PondInward[k];
                            Vector2 centre = corner + (inPrev + inK) * radius;
                            int samples = Mathf.Max(3, Mathf.CeilToInt(radius * Mathf.PI * 0.5f / style.PondPlankLength));
                            for (int s = 0; s <= samples; s++)
                            {
                                float a = s * Mathf.PI * 0.5f / samples;
                                Vector2 u = -inPrev * Mathf.Cos(a) - inK * Mathf.Sin(a);
                                outer.Add(centre + u * radius);
                                inner.Add(centre + u * (radius - w));
                                isRim.Add(s < samples || landOut);
                                if (s > 0)
                                {
                                    Vector2 p0 = outer[outer.Count - 2], p1 = outer[outer.Count - 1];
                                    fillBuf.AddTriangle(To3(corner, 0f), To3(p0, 0f), To3(p1, 0f), Vector3.up, sand, sand, sand);
                                }
                            }
                            continue;
                        }
                        if (landIn || landOut)
                        {
                            outer.Add(corner);
                            inner.Add(landIn ? corner + dirOut * w : corner - dirIn * w);
                            isRim.Add(landOut);
                            continue;
                        }
                        if (IsPondDiagonalLand(in grid, c, r, k, type))
                        {
                            // Concave corner: the rims of the two side neighbours meet here — a w × w notch closes the gap.
                            outer.Add(corner); inner.Add(corner - dirIn * w); isRim.Add(true);
                            outer.Add(corner); inner.Add(corner - dirIn * w + dirOut * w); isRim.Add(true);
                            outer.Add(corner); inner.Add(corner + dirOut * w); isRim.Add(false);
                            continue;
                        }
                        outer.Add(corner); inner.Add(corner); isRim.Add(false);
                    }

                    // Water: rings from the cell centre out to the inner contour (long contour segments split), the shore factor
                    // sampled at every vertex — a plain fan would smear the shore colour along the seams between water cells.
                    int n = inner.Count;
                    var centre2 = new Vector2(c + 0.5f, r + 0.5f);
                    contour.Clear();
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 p0 = inner[i], p1 = inner[(i + 1) % n];
                        int parts = Mathf.Max(1, Mathf.CeilToInt((p1 - p0).magnitude / PondWaterStep));
                        for (int q = 0; q < parts; q++) contour.Add(Vector2.Lerp(p0, p1, (float)q / parts));
                    }
                    int m = contour.Count, ic = verts.Count;
                    verts.Add(To3(centre2, waterHeight));
                    colors.Add(new Color(1f, 1f, 1f, PondShore(in grid, centre2.x, centre2.y, type, reach, w, shoreWidth, style.PondShoreMax)));
                    for (int k = 1; k <= PondWaterRings; k++)
                    {
                        for (int i = 0; i < m; i++)
                        {
                            Vector2 q = Vector2.Lerp(centre2, contour[i], (float)k / PondWaterRings);
                            verts.Add(To3(q, waterHeight));
                            colors.Add(new Color(1f, 1f, 1f, PondShore(in grid, q.x, q.y, type, reach, w, shoreWidth, style.PondShoreMax)));
                        }
                    }
                    for (int i = 0; i < m; i++)
                    {
                        int j = (i + 1) % m;
                        AddUpTriangle(verts, tris, ic, ic + 1 + i, ic + 1 + j);
                        for (int k = 1; k < PondWaterRings; k++)
                        {
                            int a0 = ic + 1 + (k - 1) * m + i, a1 = ic + 1 + (k - 1) * m + j, b0 = a0 + m, b1 = a1 + m;
                            AddUpTriangle(verts, tris, a0, b0, b1);
                            AddUpTriangle(verts, tris, a0, b1, a1);
                        }
                    }

                    // Rim: planks across the band (seam at each plank start), outer side down to the floor, inner side into the water.
                    for (int i = 0; i < n; i++)
                    {
                        if (!isRim[i]) continue;
                        int j = (i + 1) % n;
                        Vector2 o0 = outer[i], o1 = outer[j], i0 = inner[i], i1 = inner[j];
                        float length = Mathf.Max((o1 - o0).magnitude, (i1 - i0).magnitude);
                        int planks = Mathf.Max(1, Mathf.RoundToInt(length / style.PondPlankLength));
                        Vector2 toWater = ((i0 - o0) + (i1 - o1)).normalized;
                        for (int p = 0; p < planks; p++)
                        {
                            float t0 = (float)p / planks, t1 = (float)(p + 1) / planks;
                            Vector2 a0 = Vector2.Lerp(o0, o1, t0), a1 = Vector2.Lerp(o0, o1, t1);
                            Vector2 b0 = Vector2.Lerp(i0, i1, t0), b1 = Vector2.Lerp(i0, i1, t1);
                            Vector2 mid = (a0 + a1 + b0 + b1) * 0.25f;
                            Color plank = (PondPlankHash(mid) & 1) == 0 ? light : dark;
                            float seamT = Mathf.Clamp01(PondSeam / Mathf.Max(1e-4f, (a1 - a0).magnitude + (b1 - b0).magnitude) * 2f);
                            Vector2 sa = Vector2.Lerp(a0, a1, seamT), sb = Vector2.Lerp(b0, b1, seamT);
                            rimBuf.AddQuad(To3(a0, top), seam, To3(sa, top), seam, To3(sb, top), seam, To3(b0, top), seam, Vector3.up);
                            rimBuf.AddQuad(To3(sa, top), plank, To3(a1, top), plank, To3(b1, top), Scale(plank, 0.92f), To3(sb, top), Scale(plank, 0.92f), Vector3.up);
                        }
                        Vector3 outward = new Vector3(-toWater.x, 0f, -toWater.y);
                        rimBuf.AddQuad(To3(o0, -0.01f), sideFoot, To3(o1, -0.01f), sideFoot, To3(o1, top), side, To3(o0, top), side, outward);
                        rimBuf.AddQuad(To3(i0, bankBottom), sideFoot, To3(i1, bankBottom), sideFoot, To3(i1, top), side, To3(i0, top), side, -outward);
                    }
                }
            }
            rim = rimBuf.Create(name + "Rim");
            fill = fillBuf.Create(name + "Fill");
            var normals = new List<Vector3>(verts.Count);
            for (int k = 0; k < verts.Count; k++) normals.Add(Vector3.up);
            return Create(name, verts, normals, colors, tris);
        }

        // Cell corners SW, SE, NE, NW; edge k runs from corner k to corner k + 1 (S, E, N, W) with these directions and inward normals.
        private static readonly Vector2[] PondCorner = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        private static readonly Vector2[] PondEdgeDir = { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f) };
        private static readonly Vector2[] PondInward = { new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f), new Vector2(1f, 0f) };
        private const float PondSeam = 0.018f;
        private const float PondWaterStep = 0.2f;   // longest water contour segment, cells (mesh density only)
        private const int PondWaterRings = 3;

        /// <summary>Upward triangle (Unity winding fixed), skipped if degenerate.</summary>
        private static void AddUpTriangle(List<Vector3> verts, List<int> tris, int a, int b, int c)
        {
            Vector3 cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (Mathf.Abs(cross.y) < 1e-9f) return;
            tris.Add(a);
            if (cross.y > 0f) { tris.Add(b); tris.Add(c); }
            else { tris.Add(c); tris.Add(b); }
        }

        /// <summary>True if the neighbour across edge <paramref name="edge"/> (S, E, N, W) is not <paramref name="type"/> (or off the map).</summary>
        private static bool IsPondLand(in TileGrid grid, int c, int r, int edge, TileType type)
        {
            int nc = c + (edge == 1 ? 1 : edge == 3 ? -1 : 0), nr = r + (edge == 0 ? -1 : edge == 2 ? 1 : 0);
            return !IsType(in grid, nc, nr, type);
        }

        private static bool IsPondDiagonalLand(in TileGrid grid, int c, int r, int corner, TileType type)
        {
            int nc = c + (corner == 1 || corner == 2 ? 1 : -1), nr = r + (corner >= 2 ? 1 : -1);
            return !IsType(in grid, nc, nr, type);
        }

        private static float PondShore(in TileGrid grid, float x, float z, TileType type, int reach, float rim, float width, float max)
        {
            float t = 1f - Mathf.Clamp01((DistanceToOther(in grid, x, z, type, reach) - rim) / width);
            return max * t * t * (3f - 2f * t);
        }

        private static int PondPlankHash(Vector2 p)
        {
            unchecked
            {
                uint h = (uint)Mathf.RoundToInt(p.x * 50f) * 73856093u ^ (uint)Mathf.RoundToInt(p.y * 50f) * 19349663u;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                return (int)(h >> 7);
            }
        }

        private static Vector3 To3(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        /// <summary>Distance in the XZ plane from (x, z) to the nearest cell that is not <paramref name="type"/> (out of the map counts).</summary>
        private static float DistanceToOther(in TileGrid grid, float x, float z, TileType type, int reach)
        {
            int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
            float best = float.MaxValue;
            for (int r = cz - reach; r <= cz + reach; r++)
            {
                for (int c = cx - reach; c <= cx + reach; c++)
                {
                    if (IsType(in grid, c, r, type)) continue;
                    float dx = Mathf.Max(c - x, 0f, x - (c + 1f));
                    float dz = Mathf.Max(r - z, 0f, z - (r + 1f));
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>Distance in the XZ plane from (x, z) to the edge of the nearest prop (a disc of <paramref name="propRadius"/> in its cell).</summary>
        private static float DistanceToProp(in TileGrid grid, float x, float z, bool[] props, float propRadius)
        {
            if (props == null) return float.MaxValue;
            int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
            float best = float.MaxValue;
            for (int r = cz - 1; r <= cz + 1; r++)
            {
                for (int c = cx - 1; c <= cx + 1; c++)
                {
                    if (!grid.InBounds(c, r) || !props[grid.IndexOf(c, r)]) continue;
                    float dx = x - (c + 0.5f), dz = z - (r + 0.5f);
                    float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - propRadius);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static float DistanceToType(in TileGrid grid, float x, float z, TileType type, int reach) =>
            DistanceToType(in grid, x, z, type, reach, null);

        /// <summary>Distance in the XZ plane from (x, z) to the nearest cell of <paramref name="type"/> within <paramref name="reach"/> cells (cells in <paramref name="except"/> skipped).</summary>
        private static float DistanceToType(in TileGrid grid, float x, float z, TileType type, int reach, bool[] except)
        {
            int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
            float best = float.MaxValue;
            for (int r = cz - reach; r <= cz + reach; r++)
            {
                for (int c = cx - reach; c <= cx + reach; c++)
                {
                    if (!IsType(in grid, c, r, type)) continue;
                    if (except != null && except[grid.IndexOf(c, r)]) continue;
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

        /// <summary>Slope of the patch weight around the coverage threshold (noise units → weight).</summary>
        private const float PatchNoiseGain = 2.5f;

        /// <summary>Smooth value noise in [0, 1] (bilinear over hashed lattice points, smoothstep weights); view only.</summary>
        private static float ValueNoise(float x, float z)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx);
            fz = fz * fz * (3f - 2f * fz);
            float a = Lattice(ix, iz), b = Lattice(ix + 1, iz), c = Lattice(ix, iz + 1), d = Lattice(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        private static float Lattice(int x, int z) => (CellNoise(x * 7 + 3, z * 13 + 5) + 1f) * 0.5f;

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

            /// <summary>Vertical face from the ground line a–b down to <paramref name="bottom"/>, facing <paramref name="inward"/>.</summary>
            public void AddBank(Vector3 a, Vector3 b, float bottom, Vector3 inward, Color top, Color foot)
            {
                var ab = new Vector3(a.x, bottom, a.z);
                var bb = new Vector3(b.x, bottom, b.z);
                AddTriangle(a, b, bb, inward, top, top, foot);
                AddTriangle(a, bb, ab, inward, top, foot, foot);
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
