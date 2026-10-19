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
        /// <summary>Tall-grass variants per neighbour mask (A.3c).</summary>
        public const int BushVariantCount = 2;
        /// <summary>Neighbour masks of a bush cell: W 1, E 2, S 4, N 8.</summary>
        public const int BushMaskCount = 16;

        // Tall grass (A.3c; palette: teal-emerald top, deep foot, sunlit tips).
        private static readonly Color BushTop = new Color32(0x14, 0x96, 0x5C, 0xFF);
        private static readonly Color BushTip = new Color32(0x6E, 0xE2, 0x7E, 0xFF);
        private static readonly Color BushFoot = new Color32(0x08, 0x46, 0x36, 0xFF);
        private const int BushGrid = 8;
        private const float BushHeight = 0.74f;
        private const float BushTipAmplitude = 0.28f;
        private const float BushValleyDepth = 0.12f;
        private const float BushHeightJitter = 0.09f;
        private const float BushInset = 0.04f;
        private const float BushCornerCut = 0.14f;
        private const float BushShoulder = 0.86f;
        private const float BushFootSpread = 0.03f;
        private const float BushTipBlend = 0.85f;
        private const float BushPlaneJitter = 0.035f;
        private const float BushValleyShade = 0.5f;

        // Obstacle props (A.3c): barrel, stump, boulder.
        private static readonly Color BarrelStave = new Color32(0xD0, 0x8C, 0x4C, 0xFF);
        private static readonly Color BarrelStaveDark = new Color32(0xB8, 0x76, 0x3E, 0xFF);
        private static readonly Color BarrelHoop = new Color32(0x6E, 0x6A, 0x7C, 0xFF);
        private static readonly Color BarrelHoopLight = new Color32(0x86, 0x82, 0x96, 0xFF);
        private static readonly Color BarrelRim = new Color32(0xA8, 0x6A, 0x36, 0xFF);
        private static readonly Color BarrelLid = new Color32(0xE6, 0xAE, 0x6C, 0xFF);
        private static readonly Color BarrelLidDark = new Color32(0xD4, 0x98, 0x58, 0xFF);
        private const int BarrelSides = 14;
        private static readonly Color StumpBark = new Color32(0x8A, 0x5A, 0x36, 0xFF);
        private static readonly Color StumpBarkDark = new Color32(0x72, 0x48, 0x2A, 0xFF);
        private static readonly Color StumpBarkLight = new Color32(0x9E, 0x6A, 0x40, 0xFF);
        private static readonly Color StumpWood = new Color32(0xF2, 0xD3, 0x98, 0xFF);
        private static readonly Color StumpWoodRing = new Color32(0xD6, 0xA8, 0x68, 0xFF);
        private static readonly Color MushroomCap = new Color32(0xF0, 0x5A, 0x48, 0xFF);
        private const int StumpSides = 12;
        private static readonly Color Stone = new Color32(0xB4, 0xB0, 0xC2, 0xFF);
        private static readonly Color StoneDark = new Color32(0x9C, 0x98, 0xAE, 0xFF);
        private static readonly Color Moss = new Color32(0x8C, 0xC4, 0x4E, 0xFF);
        private static readonly Color MossLight = new Color32(0xA2, 0xD4, 0x5E, 0xFF);
        private const float BoulderRadius = 0.47f;
        private const float BoulderHeight = 0.62f;
        private const float BoulderLift = 0.18f;
        private const float BoulderJitter = 0.08f;
        private const float BoulderMossSlope = 0.55f;
        private const float PropFootShade = 0.7f;

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

        /// <summary>
        /// Tall-grass clump of one cell (A.3c, D-126): a faceted field of grass tips over the cell (<see cref="BushGrid"/>² quads,
        /// tips on the even vertices of a checker, valleys between) — flat-shaded, so every facet reads as a lit or shaded blade —
        /// plus a sawtooth skirt down to the ground on every open side. <paramref name="mask"/> = bush neighbours (W 1, E 2, S 4,
        /// N 8): towards a neighbour the field runs to the cell edge with variant-independent heights, so neighbouring clumps join
        /// into one mass without a seam; open sides are inset, lowered (a rounded shoulder) and closed by the skirt.
        /// </summary>
        public static Mesh BuildBush(int mask, int variant)
        {
            var m = new MeshData();
            var rng = new System.Random(1000 + mask * 131 + variant * 7919);
            bool w = (mask & 1) != 0, e = (mask & 2) != 0, s = (mask & 4) != 0, n = (mask & 8) != 0;
            int g = BushGrid;
            var pos = new Vector3[(g + 1) * (g + 1)];
            var tip = new bool[pos.Length];
            float x0 = -0.5f + (w ? 0f : BushInset), x1 = 0.5f - (e ? 0f : BushInset);
            float z0 = -0.5f + (s ? 0f : BushInset), z1 = 0.5f - (n ? 0f : BushInset);
            for (int j = 0; j <= g; j++)
            {
                for (int i = 0; i <= g; i++)
                {
                    float u = (float)i / g, v = (float)j / g;
                    float x = Mathf.Lerp(x0, x1, u), z = Mathf.Lerp(z0, z1, v);
                    bool onW = i == 0, onE = i == g, onS = j == 0, onN = j == g;
                    bool openEdge = (onW && !w) || (onE && !e) || (onS && !s) || (onN && !n);
                    bool sharedEdge = (onW && w) || (onE && e) || (onS && s) || (onN && n);
                    // Round the corners between two open sides.
                    bool cornerOpen = (onW || onE) && (onS || onN) && openEdge && !sharedEdge &&
                                      ((onW && !w) || (onE && !e)) && ((onS && !s) || (onN && !n));
                    if (cornerOpen)
                    {
                        x *= 1f - BushCornerCut;
                        z *= 1f - BushCornerCut;
                    }
                    bool isTip = ((i + j) & 1) == 0;
                    float h = BushHeight + (isTip ? BushTipAmplitude : -BushValleyDepth);
                    if (!sharedEdge && !openEdge)
                    {
                        h += Range(rng, -BushHeightJitter, BushHeightJitter);
                        x += Range(rng, -BushPlaneJitter, BushPlaneJitter);
                        z += Range(rng, -BushPlaneJitter, BushPlaneJitter);
                    }
                    if (openEdge) h *= BushShoulder;
                    else if (NextToOpen(i, j, g, w, e, s, n)) h *= Mathf.Lerp(BushShoulder, 1f, 0.6f);
                    int k = j * (g + 1) + i;
                    pos[k] = new Vector3(x, h, z);
                    tip[k] = isTip;
                }
            }

            for (int j = 0; j < g; j++)
            {
                for (int i = 0; i < g; i++)
                {
                    int a = j * (g + 1) + i, b = a + 1, c = a + g + 1, d = c + 1;
                    // Split along the diagonal through the two tips of the quad (or the valleys): ridges, not a flat checker.
                    if (tip[a])
                    {
                        BushTri(m, pos[a], pos[b], pos[d], tip[a], tip[b], tip[d]);
                        BushTri(m, pos[a], pos[d], pos[c], tip[a], tip[d], tip[c]);
                    }
                    else
                    {
                        BushTri(m, pos[a], pos[b], pos[c], tip[a], tip[b], tip[c]);
                        BushTri(m, pos[b], pos[d], pos[c], tip[b], tip[d], tip[c]);
                    }
                }
            }

            // Skirts on the open sides: from the field's edge down to the ground, a little wider at the foot.
            if (!s) for (int i = 0; i < g; i++) BushSkirt(m, pos[i], pos[i + 1], tip[i], tip[i + 1], Vector3.back);
            if (!n) for (int i = 0; i < g; i++) BushSkirt(m, pos[g * (g + 1) + i], pos[g * (g + 1) + i + 1], tip[g * (g + 1) + i], tip[g * (g + 1) + i + 1], Vector3.forward);
            if (!w) for (int j = 0; j < g; j++) BushSkirt(m, pos[j * (g + 1)], pos[(j + 1) * (g + 1)], tip[j * (g + 1)], tip[(j + 1) * (g + 1)], Vector3.left);
            if (!e) for (int j = 0; j < g; j++) BushSkirt(m, pos[j * (g + 1) + g], pos[(j + 1) * (g + 1) + g], tip[j * (g + 1) + g], tip[(j + 1) * (g + 1) + g], Vector3.right);
            return m.Create("Bush_" + mask + "_" + variant);
        }

        private static bool NextToOpen(int i, int j, int g, bool w, bool e, bool s, bool n) =>
            (i == 1 && !w) || (i == g - 1 && !e) || (j == 1 && !s) || (j == g - 1 && !n);

        /// <summary>Colour of a grass vertex: dark foot → juicy body → bright tips; valleys a little darker. Alpha = sway weight.</summary>
        private static Color BushColor(Vector3 p, bool isTip)
        {
            float h = Mathf.Clamp01(p.y / (BushHeight + BushTipAmplitude));
            Color col = Color.Lerp(BushFoot, BushTop, Smoothstep(0.05f, 0.8f, h));
            if (isTip) col = Color.Lerp(col, BushTip, BushTipBlend * Smoothstep(0.7f, 1f, h));
            else col = Color.Lerp(col, BushFoot, BushValleyShade);
            col = col.linear;
            col.a = Mathf.Pow(h, 1.5f);
            return col;
        }

        private static void BushTri(MeshData m, Vector3 a, Vector3 b, Vector3 c, bool ta, bool tb, bool tc) =>
            m.FlatTriColors(a, b, c, Vector3.up, BushColor(a, ta), BushColor(b, tb), BushColor(c, tc));

        private static void BushSkirt(MeshData m, Vector3 a, Vector3 b, bool ta, bool tb, Vector3 outward)
        {
            Vector3 fa = new Vector3(a.x, 0f, a.z) + outward * BushFootSpread, fb = new Vector3(b.x, 0f, b.z) + outward * BushFootSpread;
            Color ca = BushColor(a, ta), cb = BushColor(b, tb), foot = BushColor(fa, false);
            Vector3 towards = (outward + Vector3.up * 0.2f).normalized;
            m.FlatTriColors(fa, a, b, towards, foot, ca, cb);
            m.FlatTriColors(fa, b, fb, towards, foot, cb, foot);
        }

        /// <summary>
        /// Obstacle props (A.3c, D-126): a small wall group may be drawn as one of these instead of stone blocks (view only, the
        /// cell still blocks fully). Untextured (uv0.x = 1): the palette is in the vertex colours; dark foot = contact occlusion.
        /// </summary>
        public static Mesh[] BuildObstacleProps() => new[] { BuildBarrel(), BuildStump(), BuildBoulder() };

        public const int ObstaclePropCount = 3;

        public static Mesh BuildBarrel()
        {
            var m = new MeshData { Plain = 1f };
            // Profile (radius, height) bottom → top; segments 1 and 5 are iron hoops.
            Vector2[] profile =
            {
                new Vector2(0.39f, 0f), new Vector2(0.43f, 0.16f), new Vector2(0.445f, 0.24f), new Vector2(0.47f, 0.5f),
                new Vector2(0.445f, 0.76f), new Vector2(0.43f, 0.84f), new Vector2(0.39f, 1.0f), new Vector2(0.36f, 1.0f)
            };
            for (int i = 0; i + 1 < profile.Length; i++)
            {
                bool hoop = i == 1 || i == 4;
                for (int k = 0; k < BarrelSides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / BarrelSides, a1 = (k + 1) * Mathf.PI * 2f / BarrelSides;
                    Vector3 p00 = Ring(profile[i], a0), p01 = Ring(profile[i], a1), p10 = Ring(profile[i + 1], a0), p11 = Ring(profile[i + 1], a1);
                    Vector3 mid = (p00 + p11) * 0.5f;
                    Vector3 outward = i == profile.Length - 2 ? Vector3.up : new Vector3(mid.x, 0f, mid.z).normalized;
                    Color c = hoop ? (k % 2 == 0 ? BarrelHoop : BarrelHoopLight) : (i == profile.Length - 2 ? BarrelRim : (k % 2 == 0 ? BarrelStave : BarrelStaveDark));
                    m.Quad(p00, p01, p11, p10, outward, c);
                }
            }
            // Lid: slightly sunken, with a darker centre plank line.
            for (int k = 0; k < BarrelSides; k++)
            {
                float a0 = k * Mathf.PI * 2f / BarrelSides, a1 = (k + 1) * Mathf.PI * 2f / BarrelSides;
                m.FlatTri(new Vector3(0f, 0.97f, 0f), Ring(new Vector2(0.36f, 1.0f), a0), Ring(new Vector2(0.36f, 1.0f), a1), Vector3.up,
                    (k / 2) % 2 == 0 ? BarrelLid : BarrelLidDark);
            }
            m.ShadeFoot(0.3f, PropFootShade);
            return m.Create("Barrel");
        }

        public static Mesh BuildStump()
        {
            var m = new MeshData { Plain = 1f };
            // Bark with root flare; top = annual rings.
            Vector2[] profile = { new Vector2(0.49f, 0f), new Vector2(0.45f, 0.1f), new Vector2(0.42f, 0.3f), new Vector2(0.41f, 0.7f), new Vector2(0.42f, 0.86f) };
            float top = profile[profile.Length - 1].y;
            for (int i = 0; i + 1 < profile.Length; i++)
            {
                for (int k = 0; k < StumpSides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / StumpSides, a1 = (k + 1) * Mathf.PI * 2f / StumpSides;
                    // Roots: every third side bulges at the foot.
                    Vector2 r0 = profile[i], r1 = profile[i + 1];
                    Vector3 p00 = Ring(r0, a0), p01 = Ring(r0, a1), p10 = Ring(r1, a0), p11 = Ring(r1, a1);
                    Vector3 mid = (p00 + p11) * 0.5f;
                    Color c = k % 3 == 0 ? StumpBarkDark : (k % 3 == 1 ? StumpBark : StumpBarkLight);
                    m.Quad(p00, p01, p11, p10, new Vector3(mid.x, 0f, mid.z).normalized, c);
                }
            }
            // Rim of bark, then rings inwards.
            float[] ringRadii = { 0.42f, 0.37f, 0.29f, 0.21f, 0.13f, 0.06f };
            for (int r = 0; r + 1 < ringRadii.Length; r++)
            {
                Color c = r == 0 ? StumpBarkLight : (r % 2 == 1 ? StumpWood : StumpWoodRing);
                for (int k = 0; k < StumpSides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / StumpSides, a1 = (k + 1) * Mathf.PI * 2f / StumpSides;
                    float y0 = r == 0 ? top : top - 0.01f;
                    m.Quad(Ring(new Vector2(ringRadii[r], y0), a0), Ring(new Vector2(ringRadii[r], y0), a1),
                        Ring(new Vector2(ringRadii[r + 1], top - 0.01f), a1), Ring(new Vector2(ringRadii[r + 1], top - 0.01f), a0), Vector3.up, c);
                }
            }
            for (int k = 0; k < StumpSides; k++)
            {
                float a0 = k * Mathf.PI * 2f / StumpSides, a1 = (k + 1) * Mathf.PI * 2f / StumpSides;
                m.FlatTri(new Vector3(0f, top - 0.01f, 0f), Ring(new Vector2(0.06f, top - 0.01f), a0), Ring(new Vector2(0.06f, top - 0.01f), a1), Vector3.up, StumpWoodRing);
            }
            // A small mushroom at the foot (friendly detail).
            m.Sphere(new Vector3(0.3f, 0.16f, -0.36f), 0.07f, MushroomCap, 0f);
            m.ShadeFoot(0.3f, PropFootShade);
            return m.Create("Stump");
        }

        public static Mesh BuildBoulder()
        {
            var m = new MeshData { Plain = 1f };
            var rng = new System.Random(4242);
            const int Lon = 9, Lat = 5;
            var ring = new Vector3[(Lat + 1) * Lon];
            for (int j = 0; j <= Lat; j++)
            {
                // Polar angle from the top to just below the equator (the foot is hidden in the ground).
                float t = Mathf.Lerp(0f, 100f, (float)j / Lat) * Mathf.Deg2Rad;
                for (int k = 0; k < Lon; k++)
                {
                    float a = (k + (j % 2) * 0.5f) * Mathf.PI * 2f / Lon;
                    float jitter = j == 0 ? 1f : 1f + Range(rng, -BoulderJitter, BoulderJitter);
                    var dir = new Vector3(Mathf.Sin(t) * Mathf.Cos(a), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(a));
                    ring[j * Lon + k] = new Vector3(dir.x * BoulderRadius * jitter, BoulderLift + dir.y * BoulderHeight * jitter, dir.z * BoulderRadius * jitter);
                }
            }
            for (int j = 0; j < Lat; j++)
            {
                for (int k = 0; k < Lon; k++)
                {
                    Vector3 a = ring[j * Lon + k], b = ring[j * Lon + (k + 1) % Lon];
                    Vector3 c = ring[(j + 1) * Lon + k], d = ring[(j + 1) * Lon + (k + 1) % Lon];
                    if (j == Lat - 1) { c.y = Mathf.Min(c.y, 0f); d.y = Mathf.Min(d.y, 0f); }
                    BoulderTri(m, a, b, c, j, rng);
                    BoulderTri(m, b, d, c, j, rng);
                }
            }
            m.ShadeFoot(0.35f, PropFootShade);
            return m.Create("Boulder");
        }

        private static void BoulderTri(MeshData m, Vector3 a, Vector3 b, Vector3 c, int band, System.Random rng)
        {
            Vector3 centre = (a + b + c) / 3f;
            Vector3 outward = new Vector3(centre.x, centre.y - BoulderLift, centre.z);
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, outward) < 0f) nrm = -nrm;
            nrm.Normalize();
            // Moss on the upward facets of the cap, stone (two tones) elsewhere.
            Color col = nrm.y > BoulderMossSlope && band < 2 ? (rng.NextDouble() < 0.5 ? Moss : MossLight)
                : (rng.NextDouble() < 0.5 ? Stone : StoneDark);
            m.FlatTri(a, b, c, outward, col);
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

            /// <summary>Flat triangle with a linear colour per corner (alpha kept).</summary>
            public void FlatTriColors(Vector3 a, Vector3 b, Vector3 c, Vector3 towards, Color ca, Color cb, Color cc)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(n, towards) < 0f) { (b, c) = (c, b); (cb, cc) = (cc, cb); n = -n; }
                n.Normalize();
                int i = Add(a, n, ca);
                Add(b, n, cb);
                Add(c, n, cc);
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
