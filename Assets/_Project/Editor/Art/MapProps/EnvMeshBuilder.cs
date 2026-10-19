using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Procedural meshes of map props (Art-Spec §2.1, A.3b, D-125), pivot at the bottom centre of a 1 × 1 cell, vertex colours
    /// linear (palette hex in sRGB → <see cref="Color.linear"/>):
    /// bush — a cap of squashed blobs, ≤ <see cref="BushTriangleBudget"/> triangles, alpha = sway weight (0 at the ground);
    /// crate (breakable wall) — panels, a dark frame and an X brace on every side and on top, alpha = gloss 0;
    /// berry basket (Last Beast box) — a lathe basket with a rim and glossy berries (alpha = gloss 1) on top; berries, leaves and
    /// the inside have uv0.x = 1 (no texture detail, Arena/ToonEnv).
    /// </summary>
    public static class EnvMeshBuilder
    {
        public const int BushTriangleBudget = 250;
        public const int BushVariantCount = 3;

        // Bush (palette: top #2FAE4F, foot #1C7A38).
        private static readonly Color BushTop = new Color32(0x2F, 0xAE, 0x4F, 0xFF);
        private static readonly Color BushTip = new Color32(0x5C, 0xCB, 0x5A, 0xFF);
        private static readonly Color BushFoot = new Color32(0x1C, 0x7A, 0x38, 0xFF);
        private static readonly Color FlowerWhite = new Color32(0xFF, 0xF6, 0xE8, 0xFF);
        private static readonly Color FlowerYellow = new Color32(0xFF, 0xD8, 0x4A, 0xFF);
        private static readonly Color FlowerLilac = new Color32(0xC9, 0xA6, 0xF2, 0xFF);
        private const float BushHeight = 0.86f;
        private const int BlobLongitudes = 8;
        private const float BlobJitter = 0.07f;
        private const float SpherifyNormals = 0.55f;
        private const float FlowerSize = 0.045f;

        // Crate (palette: planks #E4A85E, dark edge #9A6232).
        private static readonly Color CratePanel = new Color32(0xE4, 0xA8, 0x5E, 0xFF);
        private static readonly Color CrateFrame = new Color32(0x9A, 0x62, 0x32, 0xFF);
        private static readonly Color CrateBrace = new Color32(0xC0, 0x80, 0x44, 0xFF);
        private const float CrateHalf = 0.39f;
        private const float CrateHeight = 1.0f;
        private const float CrateBeam = 0.1f;
        private const float CrateFootShade = 0.78f;

        // Basket (palette: wicker #D9A55B, berry #E0306A).
        private static readonly Color Wicker = new Color32(0xD9, 0xA5, 0x5B, 0xFF);
        private static readonly Color WickerRim = new Color32(0xEC, 0xC0, 0x78, 0xFF);
        private static readonly Color BasketInside = new Color32(0x6E, 0x2A, 0x3A, 0xFF);
        private static readonly Color Berry = new Color32(0xE0, 0x30, 0x6A, 0xFF);
        private static readonly Color Blueberry = new Color32(0x4B, 0x5B, 0xD6, 0xFF);
        private static readonly Color Leaf = new Color32(0x3C, 0xB8, 0x52, 0xFF);
        private const int BasketSides = 12;

        public static Mesh BuildBush(int variant)
        {
            var m = new MeshData();
            var rng = new System.Random(1000 + variant * 7919);
            // Blobs: (centre, horizontal radius, vertical radius). One big crown + a ring; variants 1 and 2 have flowers.
            int ring = variant == 0 ? 5 : 4;
            var blobs = new List<(Vector3 c, float rh, float rv)> { (new Vector3(Range(rng, -0.04f, 0.04f), 0.44f, Range(rng, -0.04f, 0.04f)), 0.36f, 0.38f) };
            float start = Range(rng, 0f, Mathf.PI * 2f);
            for (int i = 0; i < ring; i++)
            {
                float a = start + i * Mathf.PI * 2f / ring + Range(rng, -0.25f, 0.25f);
                float d = Range(rng, 0.29f, 0.34f);
                blobs.Add((new Vector3(Mathf.Cos(a) * d, Range(rng, 0.26f, 0.34f), Mathf.Sin(a) * d), Range(rng, 0.25f, 0.31f), Range(rng, 0.25f, 0.3f)));
            }
            var centre = new Vector3(0f, 0.18f, 0f);
            for (int b = 0; b < blobs.Count; b++)
                AddBlob(m, blobs[b].c, blobs[b].rh, blobs[b].rv, centre, rng, b == 0);
            if (variant > 0)
            {
                Color petal = variant == 1 ? FlowerWhite : FlowerLilac;
                for (int f = 0; f < 6; f++)
                {
                    var (c, rh, rv) = blobs[f % blobs.Count];
                    float a = Range(rng, 0f, Mathf.PI * 2f), up = Range(rng, 0.35f, 0.75f);
                    var n = new Vector3(Mathf.Cos(a) * Mathf.Sqrt(1f - up * up), up, Mathf.Sin(a) * Mathf.Sqrt(1f - up * up));
                    var p = c + new Vector3(n.x * rh, n.y * rv, n.z * rh);
                    AddFlower(m, p, f % 3 == 0 ? FlowerYellow : petal);
                }
            }
            return m.Create("Bush_" + variant);
        }

        /// <summary>UV-sphere blob without the bottom cap (never seen): 8 longitudes, rings at 50°, 95°, 135° from the top.</summary>
        private static void AddBlob(MeshData m, Vector3 c, float rh, float rv, Vector3 bushCentre, System.Random rng, bool crown)
        {
            float[] polar = { 0f, 50f, 95f, 135f };
            int first = m.Verts.Count;
            for (int ringIndex = 0; ringIndex < polar.Length; ringIndex++)
            {
                float t = polar[ringIndex] * Mathf.Deg2Rad;
                int count = ringIndex == 0 ? 1 : BlobLongitudes;
                for (int k = 0; k < count; k++)
                {
                    float a = (k + (ringIndex % 2) * 0.5f) * Mathf.PI * 2f / BlobLongitudes;
                    var dir = new Vector3(Mathf.Sin(t) * Mathf.Cos(a), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(a));
                    float j = 1f + Range(rng, -BlobJitter, BlobJitter);
                    var p = c + new Vector3(dir.x * rh * j, dir.y * rv * j, dir.z * rh * j);
                    Vector3 blobNormal = new Vector3(dir.x / rh, dir.y / rv, dir.z / rh).normalized;
                    Vector3 n = Vector3.Lerp(blobNormal, (p - bushCentre).normalized, SpherifyNormals).normalized;
                    float h = Mathf.Clamp01(p.y / BushHeight);
                    Color col = Color.Lerp(BushFoot, BushTop, Smoothstep(0.1f, 0.75f, h));
                    if (crown) col = Color.Lerp(col, BushTip, Smoothstep(0.8f, 1f, h));
                    col = col.linear;
                    col.a = Mathf.Pow(h, 1.5f);
                    m.Add(p, n, col);
                }
            }
            // Top cap.
            for (int k = 0; k < BlobLongitudes; k++)
                m.Tri(first, first + 1 + (k + 1) % BlobLongitudes, first + 1 + k);
            // Bands between rings (odd rings are turned by half a step).
            for (int ringIndex = 1; ringIndex < polar.Length - 1; ringIndex++)
            {
                int a0 = first + 1 + (ringIndex - 1) * BlobLongitudes, b0 = a0 + BlobLongitudes;
                for (int k = 0; k < BlobLongitudes; k++)
                {
                    int a = a0 + k, a1 = a0 + (k + 1) % BlobLongitudes, b = b0 + k, b1 = b0 + (k + 1) % BlobLongitudes;
                    m.Tri(a, a1, b);
                    m.Tri(a1, b1, b);
                }
            }
        }

        /// <summary>A tiny four-petal pyramid on the bush surface (4 triangles), sways like the top.</summary>
        private static void AddFlower(MeshData m, Vector3 p, Color petal)
        {
            Color c = petal.linear;
            c.a = 1f;
            int tip = m.Add(p + Vector3.up * FlowerSize * 0.7f, Vector3.up, c);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + 0.4f;
                m.Add(p + new Vector3(Mathf.Cos(a), -0.15f, Mathf.Sin(a)) * FlowerSize, Vector3.up, c);
            }
            for (int k = 0; k < 4; k++) m.Tri(tip, tip + 1 + (k + 1) % 4, tip + 1 + k);
        }

        public static Mesh BuildCrate()
        {
            var m = new MeshData();
            float h = CrateHalf, top = CrateHeight, beam = CrateBeam, o = h + beam * 0.5f;
            m.Box(new Vector3(-h, 0f, -h), new Vector3(h, top - 0.02f, h), CratePanel, true);
            // Corner posts, top and bottom rims.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    m.Box(new Vector3(sx * o - beam * 0.5f, 0f, sz * o - beam * 0.5f), new Vector3(sx * o + beam * 0.5f, top + 0.02f, sz * o + beam * 0.5f), CrateFrame, true);
            foreach (float y in new[] { 0f, top - beam })
            {
                m.Box(new Vector3(-o, y, -o - beam * 0.5f), new Vector3(o, y + beam, -o + beam * 0.5f), CrateFrame, true);
                m.Box(new Vector3(-o, y, o - beam * 0.5f), new Vector3(o, y + beam, o + beam * 0.5f), CrateFrame, true);
                m.Box(new Vector3(-o - beam * 0.5f, y, -o), new Vector3(-o + beam * 0.5f, y + beam, o), CrateFrame, true);
                m.Box(new Vector3(o - beam * 0.5f, y, -o), new Vector3(o + beam * 0.5f, y + beam, o), CrateFrame, true);
            }
            // X braces on the four sides and on top.
            float panelW = h, panelH = (top - 2f * beam) * 0.5f, midY = top * 0.5f, depth = 0.025f;
            float len = Mathf.Sqrt(panelW * panelW + panelH * panelH), angle = Mathf.Atan2(panelH, panelW);
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            foreach (Vector3 n in normals)
            {
                Vector3 side = Vector3.Cross(Vector3.up, n);
                Vector3 c = n * (h + depth * 0.5f) + Vector3.up * midY;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 u = (side * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle) * s).normalized;
                    Vector3 v = Vector3.Cross(n, u).normalized;
                    m.OrientedBox(c, u * len * 2f * 0.94f, v * beam * 0.45f, n * depth, CrateBrace);
                }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 u = new Vector3(1f, 0f, s).normalized;
                Vector3 v = Vector3.Cross(Vector3.up, u).normalized;
                m.OrientedBox(new Vector3(0f, top - 0.02f + depth * 0.5f, 0f), u * h * 2f * 1.41f * 0.94f, v * beam * 0.45f, Vector3.up * depth, CrateBrace);
            }
            m.ShadeFoot(0.45f, CrateFootShade);
            return m.Create("Crate");
        }

        public static Mesh BuildBasket()
        {
            var m = new MeshData();
            // Profile (radius, height) from the bottom to the rim; then a rim band.
            Vector2[] profile = { new Vector2(0.30f, 0f), new Vector2(0.37f, 0.07f), new Vector2(0.42f, 0.5f), new Vector2(0.45f, 0.54f), new Vector2(0.45f, 0.62f), new Vector2(0.39f, 0.62f) };
            for (int i = 0; i + 1 < profile.Length; i++)
            {
                Color c = i >= 2 ? WickerRim : Wicker;
                for (int k = 0; k < BasketSides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / BasketSides, a1 = (k + 1) * Mathf.PI * 2f / BasketSides;
                    Vector3 p00 = Ring(profile[i], a0), p01 = Ring(profile[i], a1), p10 = Ring(profile[i + 1], a0), p11 = Ring(profile[i + 1], a1);
                    Vector3 mid = (p00 + p11) * 0.5f;
                    Vector3 outward = i == profile.Length - 2 ? Vector3.up : new Vector3(mid.x, 0f, mid.z).normalized;
                    m.Quad(p00, p01, p11, p10, outward, c);
                }
            }
            // Inside (dark) under the berries; berries and leaves are not textured (uv0.x = 1, see Arena/ToonEnv).
            m.Plain = 1f;
            float fill = 0.6f;
            for (int k = 0; k < BasketSides; k++)
            {
                float a0 = k * Mathf.PI * 2f / BasketSides, a1 = (k + 1) * Mathf.PI * 2f / BasketSides;
                m.FlatTri(new Vector3(0f, fill, 0f), Ring(new Vector2(0.39f, fill), a0), Ring(new Vector2(0.39f, fill), a1), Vector3.up, BasketInside);
            }
            // A mound of berries (glossy) and two leaves.
            var rng = new System.Random(31);
            Vector3[] berries =
            {
                new Vector3(0f, 0.74f, 0f), new Vector3(0.17f, 0.68f, 0.05f), new Vector3(-0.15f, 0.68f, 0.1f), new Vector3(0.03f, 0.68f, -0.17f),
                new Vector3(-0.1f, 0.67f, -0.14f), new Vector3(0.12f, 0.67f, -0.12f), new Vector3(0.06f, 0.69f, 0.17f), new Vector3(-0.2f, 0.66f, -0.03f)
            };
            for (int i = 0; i < berries.Length; i++)
                m.Sphere(berries[i], Range(rng, 0.085f, 0.105f), i % 3 == 2 ? Blueberry : Berry, 1f);
            m.Leaf(new Vector3(0.05f, 0.82f, 0.02f), new Vector3(0.22f, 0.04f, 0.1f), Leaf);
            m.Leaf(new Vector3(0.0f, 0.82f, 0.0f), new Vector3(-0.12f, 0.05f, 0.2f), Leaf);
            m.ShadeFoot(0.3f, CrateFootShade);
            return m.Create("BerryBasket");
        }

        /// <summary>GLSL-style smoothstep: 0 below <paramref name="e0"/>, 1 above <paramref name="e1"/> (Mathf.SmoothStep interpolates values instead).</summary>
        private static float Smoothstep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static Vector3 Ring(Vector2 rh, float a) => new Vector3(Mathf.Cos(a) * rh.x, rh.y, Mathf.Sin(a) * rh.x);

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Vertex lists of one mesh; colours given in sRGB to the helpers below are converted to linear here.</summary>
        private sealed class MeshData
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();

            /// <summary>uv0.x of the next vertices: 1 = no texture detail in Arena/ToonEnv (berries, leaves), 0 = textured.</summary>
            public float Plain;
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<int> _tris = new List<int>();

            /// <summary>Adds a vertex (colour already linear); returns its index.</summary>
            public int Add(Vector3 p, Vector3 n, Color linear)
            {
                Verts.Add(p);
                _uvs.Add(new Vector2(Plain, 0f));
                _normals.Add(n);
                _colors.Add(linear);
                return Verts.Count - 1;
            }

            public void Tri(int a, int b, int c)
            {
                // Clockwise seen from outside = front face in Unity; fix by the outward normal of the first vertex.
                Vector3 n = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
                Vector3 avg = _normals[a] + _normals[b] + _normals[c];
                if (Vector3.Dot(n, avg) < 0f) { _tris.Add(a); _tris.Add(c); _tris.Add(b); }
                else { _tris.Add(a); _tris.Add(b); _tris.Add(c); }
            }

            public void FlatTri(Vector3 a, Vector3 b, Vector3 c, Vector3 towards, Color srgb, float gloss = 0f)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(n, towards) < 0f) { (b, c) = (c, b); n = -n; }
                n.Normalize();
                Color col = srgb.linear;
                col.a = gloss;
                int i = Add(a, n, col);
                Add(b, n, col);
                Add(c, n, col);
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 towards, Color srgb)
            {
                FlatTri(a, b, c, towards, srgb);
                FlatTri(a, c, d, towards, srgb);
            }

            /// <summary>Axis-aligned box without the bottom face.</summary>
            public void Box(Vector3 min, Vector3 max, Color srgb, bool skipBottom)
            {
                Vector3 c = (min + max) * 0.5f, e = (max - min) * 0.5f;
                OrientedBox(c, Vector3.right * e.x * 2f, Vector3.forward * e.z * 2f, Vector3.up * e.y * 2f, srgb, skipBottom);
            }

            /// <summary>Box centred at <paramref name="c"/> with full edge vectors u, v, w (w = "up" of the box for the skipped face).</summary>
            public void OrientedBox(Vector3 c, Vector3 u, Vector3 v, Vector3 w, Color srgb, bool skipBottom = false)
            {
                Vector3 hu = u * 0.5f, hv = v * 0.5f, hw = w * 0.5f;
                Vector3[] axes = { hu, -hu, hv, -hv, hw, -hw };
                for (int f = 0; f < 6; f++)
                {
                    if (skipBottom && f == 5) continue;
                    Vector3 n = axes[f];
                    Vector3 a1 = f < 2 ? hv : hu, a2 = f < 4 ? hw : hv;
                    Vector3 fc = c + n;
                    Quad(fc - a1 - a2, fc + a1 - a2, fc + a1 + a2, fc - a1 + a2, n, srgb);
                }
            }

            /// <summary>Low-poly sphere (6 longitudes, 4 bands), smooth normals, alpha = gloss.</summary>
            public void Sphere(Vector3 c, float r, Color srgb, float gloss)
            {
                const int Lon = 6, Lat = 4;
                Color col = srgb.linear;
                col.a = gloss;
                int first = Verts.Count;
                for (int j = 0; j <= Lat; j++)
                {
                    float t = j * Mathf.PI / Lat;
                    for (int k = 0; k < Lon; k++)
                    {
                        float a = k * Mathf.PI * 2f / Lon;
                        var n = new Vector3(Mathf.Sin(t) * Mathf.Cos(a), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(a));
                        Add(c + n * r, n, col);
                    }
                }
                for (int j = 0; j < Lat; j++)
                    for (int k = 0; k < Lon; k++)
                    {
                        int a = first + j * Lon + k, b = first + j * Lon + (k + 1) % Lon, d = a + Lon, e = b + Lon;
                        if (j > 0) Tri(a, b, d);
                        if (j < Lat - 1) Tri(b, e, d);
                    }
            }

            /// <summary>Flat double-sided diamond leaf from <paramref name="root"/> along <paramref name="dir"/>.</summary>
            public void Leaf(Vector3 root, Vector3 dir, Color srgb)
            {
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * dir.magnitude * 0.3f;
                Vector3 mid = root + dir * 0.5f + Vector3.up * 0.03f, tip = root + dir;
                FlatTri(root, mid + side, tip, Vector3.up, srgb);
                FlatTri(root, tip, mid - side, Vector3.up, srgb);
            }

            /// <summary>Darkens vertices near the ground: × <paramref name="shade"/> at y = 0 → × 1 at <paramref name="height"/>.</summary>
            public void ShadeFoot(float height, float shade)
            {
                for (int i = 0; i < Verts.Count; i++)
                {
                    float k = Mathf.Lerp(shade, 1f, Mathf.Clamp01(Verts[i].y / height));
                    Color c = _colors[i];
                    _colors[i] = new Color(c.r * k, c.g * k, c.b * k, c.a);
                }
            }

            public Mesh Create(string name)
            {
                var mesh = new Mesh { name = name };
                if (Verts.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Verts);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                if (Plain > 0f || _uvs.Exists(uv => uv.x > 0f)) mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
