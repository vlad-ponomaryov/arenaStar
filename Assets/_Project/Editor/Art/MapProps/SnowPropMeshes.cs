using System.Collections.Generic;
using Arena.Editor.Art.Procedural;
using UnityEngine;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Procedural props of the snow theme (D-140, Map-SnowValley-LB §5 п.4), primitives only: spruce (3 cones + snow rims + baubles +
    /// a star), gifts (box + ribbon cross + bow, 3 colours; big one 1.0), snowman (3 spheres + hat + scarf, 2 scarf colours), snow
    /// bricks (soft, cracked) and fort corners (bricks on a dark wooden plinth), drift, sled, flat snow pile and frame spruces.
    /// Pivot at the bottom centre of a 1 × 1 cell; colours authored in sRGB and written linear; vertex alpha = gloss (matte 0,
    /// baubles and the star glossy); uv0.x = 1 everywhere (no texture detail, Arena/ToonEnv). No team colours (D-136).
    /// </summary>
    public static class SnowPropMeshes
    {
        private const int Segments = 14;
        private const int SmallSegments = 8;
        private const int ArcSteps = 2;
        private const float FootHeight = 0.18f;
        private const float FootShade = 0.78f;
        private const float Matte = 0f;
        private const float Glossy = 1f;

        private static readonly Color Snow = new Color32(0xFB, 0xFC, 0xFE, 0xFF);
        private static readonly Color SnowShade = new Color32(0xD2, 0xDE, 0xEC, 0xFF);
        private static readonly Color SnowBlue = new Color32(0xE4, 0xEE, 0xF8, 0xFF);
        private static readonly Color Needles = new Color32(0x2E, 0x5A, 0x3C, 0xFF);
        private static readonly Color NeedlesDark = new Color32(0x22, 0x47, 0x2F, 0xFF);
        private static readonly Color NeedlesLight = new Color32(0x3C, 0x70, 0x4C, 0xFF);
        private static readonly Color Trunk = new Color32(0x6B, 0x44, 0x26, 0xFF);
        private static readonly Color Gold = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        private static readonly Color Pearl = new Color32(0xF5, 0xF2, 0xEA, 0xFF);
        private static readonly Color Lilac = new Color32(0xA7, 0x7B, 0xD0, 0xFF);
        private static readonly Color Apricot = new Color32(0xF0, 0xA0, 0x48, 0xFF);
        private static readonly Color Mint = new Color32(0x6E, 0xCF, 0xB0, 0xFF);
        private static readonly Color GiftGold = new Color32(0xE9, 0xB2, 0x3E, 0xFF);
        private static readonly Color GiftLilac = new Color32(0x8E, 0x66, 0xC8, 0xFF);
        private static readonly Color RibbonWhite = new Color32(0xF8, 0xF4, 0xEC, 0xFF);
        private static readonly Color BigGiftBody = new Color32(0x6F, 0x4C, 0xB8, 0xFF);
        private static readonly Color Coal = new Color32(0x2A, 0x26, 0x2E, 0xFF);
        private static readonly Color Hat = new Color32(0x3A, 0x35, 0x40, 0xFF);
        private static readonly Color Carrot = new Color32(0xF0, 0x8A, 0x3C, 0xFF);
        private static readonly Color ScarfGreen = new Color32(0x3E, 0x9A, 0x5A, 0xFF);
        private static readonly Color ScarfLilac = new Color32(0x9A, 0x6C, 0xC8, 0xFF);
        private static readonly Color Brick = new Color32(0xEC, 0xF3, 0xFA, 0xFF);
        private static readonly Color BrickSoft = new Color32(0xDD, 0xEA, 0xF6, 0xFF);
        private static readonly Color BrickSeam = new Color32(0x8E, 0xA6, 0xC0, 0xFF);
        private static readonly Color Crack = new Color32(0x7C, 0x92, 0xAE, 0xFF);
        private static readonly Color Plinth = new Color32(0x5E, 0x40, 0x2A, 0xFF);
        private static readonly Color PlinthLight = new Color32(0x7A, 0x56, 0x38, 0xFF);
        private static readonly Color SledWood = new Color32(0xA2, 0x5E, 0x32, 0xFF);
        private static readonly Color SledWoodDark = new Color32(0x7A, 0x44, 0x22, 0xFF);
        private static readonly Color Runner = new Color32(0x4A, 0x46, 0x52, 0xFF);

        /// <summary>Spruce of the centre plus (Ø ≈ 3 cells, h ≈ 2.5): trunk, three needle cones with snow rims, baubles, a star.</summary>
        public static Mesh BuildTree()
        {
            var m = new SnowMesh();
            m.Cylinder(Vector3.zero, 0.24f, 0.4f, Trunk);
            // Tiers: (bottom radius, bottom height, cone height).
            Vector3[] tiers = { new Vector3(1.45f, 0.3f, 1.05f), new Vector3(1.1f, 0.9f, 0.95f), new Vector3(0.72f, 1.45f, 0.95f) };
            Color[] tones = { NeedlesDark, Needles, NeedlesLight };
            for (int t = 0; t < tiers.Length; t++)
            {
                Vector3 tier = tiers[t];
                m.Cone(new Vector3(0f, tier.y, 0f), tier.x, tier.z, tones[t]);
                m.Torus(new Vector3(0f, tier.y + 0.03f, 0f), tier.x * 0.93f, 0.075f, Snow);
                m.Cone(new Vector3(0f, tier.y + tier.z * 0.62f, 0f), tier.x * 0.33f, tier.z * 0.38f, Snow); // snow on the tip
            }
            Color[] baubles = { Gold, Pearl, Apricot, Lilac };
            for (int t = 0; t < tiers.Length; t++)
            {
                Vector3 tier = tiers[t];
                int count = 6 - t;
                for (int k = 0; k < count; k++)
                {
                    float a = (k + t * 0.5f) * Mathf.PI * 2f / count;
                    float h = 0.32f;
                    float r = tier.x * (1f - h) * 0.98f;
                    var p = new Vector3(Mathf.Cos(a) * r, tier.y + tier.z * h, Mathf.Sin(a) * r);
                    m.Sphere(p, 0.11f, baubles[(k + t) % baubles.Length], Glossy);
                }
            }
            m.Sphere(new Vector3(0f, 2.48f, 0f), 0.2f, Gold, Glossy);
            return m.Create("SnowTree", false);
        }

        /// <summary>Gift box 0.8 (big: 1.0, taller) with a ribbon cross and a bow on top.</summary>
        public static Mesh BuildGift(string name, Color body, Color ribbon, bool big)
        {
            var m = new SnowMesh();
            float half = big ? 0.48f : 0.38f, height = big ? 0.86f : 0.66f, band = big ? 0.09f : 0.07f;
            m.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(half, height * 0.5f, half), 0.06f, body);
            m.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(half + 0.012f, height * 0.5f + 0.012f, band), 0.02f, ribbon);
            m.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(band, height * 0.5f + 0.012f, half + 0.012f), 0.02f, ribbon);
            float bow = big ? 0.17f : 0.13f;
            m.Ellipsoid(new Vector3(-bow * 0.8f, height + bow * 0.45f, 0f), new Vector3(bow, bow * 0.6f, bow * 0.45f), ribbon, Matte);
            m.Ellipsoid(new Vector3(bow * 0.8f, height + bow * 0.45f, 0f), new Vector3(bow, bow * 0.6f, bow * 0.45f), ribbon, Matte);
            m.Sphere(new Vector3(0f, height + bow * 0.35f, 0f), bow * 0.45f, ribbon, Matte);
            return m.Create(name, true);
        }

        public static Mesh[] BuildGifts() => new[]
        {
            BuildGift("Gift_Mint", Mint, Gold, false),
            BuildGift("Gift_Gold", GiftGold, RibbonWhite, false),
            BuildGift("Gift_Lilac", GiftLilac, Gold, false)
        };

        public static Mesh BuildBigGift() => BuildGift("Gift_Big", BigGiftBody, Gold, true);

        /// <summary>Snowman: three snow balls, coal eyes and buttons, a carrot nose towards −Z (the camera), a hat and a scarf.</summary>
        public static Mesh BuildSnowman(string name, Color scarf)
        {
            var m = new SnowMesh();
            m.Sphere(new Vector3(0f, 0.32f, 0f), 0.38f, Snow, Matte);
            m.Sphere(new Vector3(0f, 0.8f, 0f), 0.28f, Snow, Matte);
            m.Sphere(new Vector3(0f, 1.15f, 0f), 0.2f, Snow, Matte);
            m.Torus(new Vector3(0f, 1.0f, 0f), 0.19f, 0.065f, scarf);
            m.Box(new Vector3(0.12f, 0.86f, -0.2f), new Vector3(0.05f, 0.12f, 0.025f), 0.02f, scarf);
            m.Cylinder(new Vector3(0f, 1.3f, 0f), 0.24f, 0.03f, Hat);
            m.Cylinder(new Vector3(0f, 1.3f, 0f), 0.15f, 0.22f, Hat);
            m.Sphere(new Vector3(-0.075f, 1.2f, -0.17f), 0.03f, Coal, Matte);
            m.Sphere(new Vector3(0.075f, 1.2f, -0.17f), 0.03f, Coal, Matte);
            m.ConeAlong(new Vector3(0f, 1.14f, -0.18f), Vector3.back, 0.045f, 0.16f, Carrot);
            for (int i = 0; i < 3; i++) m.Sphere(new Vector3(0f, 0.68f + i * 0.11f, -0.27f + Mathf.Abs(i - 1) * 0.015f), 0.028f, Coal, Matte);
            m.ShadeTowards(0.25f, SnowShade, Snow);
            return m.Create(name, true);
        }

        public static Mesh[] BuildSnowmen() => new[] { BuildSnowman("Snowman_Green", ScarfGreen), BuildSnowman("Snowman_Lilac", ScarfLilac) };

        /// <summary>Breakable snow bricks (soft): two rows of rounded bricks over a dark seam core, a few cracks; lower than a corner.</summary>
        public static Mesh BuildFortBrick()
        {
            var m = new SnowMesh();
            AddBrickRows(m, 0f, 0.38f, BrickSoft);
            m.Box(new Vector3(-0.12f, 0.765f, 0.1f), new Vector3(0.16f, 0.006f, 0.012f), 0.004f, Crack);
            m.Box(new Vector3(0.14f, 0.765f, -0.16f), new Vector3(0.012f, 0.006f, 0.13f), 0.004f, Crack);
            m.Box(new Vector3(0.2f, 0.25f, -0.452f), new Vector3(0.012f, 0.1f, 0.006f), 0.004f, Crack);
            return m.Create("SnowBrick", true);
        }

        /// <summary>Fort corner / pier (unbreakable): a dark wooden plinth with two rows of white bricks on it.</summary>
        public static Mesh BuildFortCorner()
        {
            var m = new SnowMesh();
            m.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.49f, 0.14f, 0.49f), 0.04f, Plinth);
            for (int k = 0; k < 4; k++)
                m.Box(new Vector3(-0.36f + k * 0.24f, 0.14f, -0.492f), new Vector3(0.005f, 0.13f, 0.005f), 0.002f, PlinthLight);
            AddBrickRows(m, 0.28f, 0.36f, Brick);
            m.Box(new Vector3(0f, 1.02f, 0f), new Vector3(0.44f, 0.04f, 0.44f), 0.04f, Snow); // snow cap
            return m.Create("SnowFortCorner", false);
        }

        private static void AddBrickRows(SnowMesh m, float baseY, float rowHeight, Color brick)
        {
            const float Gap = 0.025f;
            float top = baseY + rowHeight * 2f;
            // Dark core between the bricks: below their tops, so only the seams show it.
            m.Box(new Vector3(0f, (baseY + top) * 0.5f - 0.03f, 0f), new Vector3(0.42f, (top - baseY) * 0.5f - 0.05f, 0.42f), 0.02f, BrickSeam);
            for (int row = 0; row < 2; row++)
            {
                float y = baseY + rowHeight * (row + 0.5f);
                float hy = rowHeight * 0.5f - Gap;
                for (int k = 0; k < 2; k++)
                {
                    float offset = (k - 0.5f) * 0.5f;
                    Vector3 centre = row == 0 ? new Vector3(offset, y, 0f) : new Vector3(0f, y, offset);
                    Vector3 halfSize = row == 0 ? new Vector3(0.25f - Gap, hy, 0.48f) : new Vector3(0.48f, hy, 0.25f - Gap);
                    m.Box(centre, halfSize, 0.07f, brick);
                }
            }
        }

        /// <summary>Snow drift: a squashed snow mound with a smaller lump, bluish shade at the foot.</summary>
        public static Mesh BuildDrift()
        {
            var m = new SnowMesh();
            m.Ellipsoid(new Vector3(0f, 0.08f, 0f), new Vector3(0.6f, 0.62f, 0.56f), SnowBlue, Matte);
            m.Ellipsoid(new Vector3(0.22f, 0.12f, -0.2f), new Vector3(0.34f, 0.4f, 0.32f), Snow, Matte);
            m.ShadeTowards(0.3f, SnowShade, Snow);
            return m.Create("SnowDrift", false);
        }

        /// <summary>Sled cell (long axis along Z; two cells in a column join into one sled): runners, a slatted deck with side boards.</summary>
        public static Mesh BuildSled()
        {
            var m = new SnowMesh();
            for (int s = -1; s <= 1; s += 2)
            {
                m.Box(new Vector3(s * 0.3f, 0.04f, 0f), new Vector3(0.035f, 0.035f, 0.5f), 0.02f, Runner);
                for (int k = -1; k <= 1; k += 2)
                    m.Box(new Vector3(s * 0.3f, 0.17f, k * 0.3f), new Vector3(0.03f, 0.11f, 0.03f), 0.01f, SledWoodDark);
            }
            for (int k = 0; k < 4; k++)
                m.Box(new Vector3(0f, 0.31f, -0.375f + k * 0.25f), new Vector3(0.4f, 0.04f, 0.11f), 0.02f, k % 2 == 0 ? SledWood : SledWoodDark);
            for (int s = -1; s <= 1; s += 2)
                m.Box(new Vector3(s * 0.38f, 0.5f, 0f), new Vector3(0.04f, 0.17f, 0.5f), 0.03f, SledWood);
            m.Ellipsoid(new Vector3(0f, 0.36f, 0f), new Vector3(0.32f, 0.28f, 0.46f), Snow, Matte); // snow load
            return m.Create("SnowSled", false);
        }

        /// <summary>Flat snow pile left by a broken snowman or snow bricks (no collision).</summary>
        public static Mesh BuildSnowPile()
        {
            var m = new SnowMesh();
            m.Ellipsoid(Vector3.zero, new Vector3(0.46f, 0.14f, 0.42f), SnowBlue, Matte);
            m.Ellipsoid(new Vector3(-0.15f, 0.02f, 0.12f), new Vector3(0.2f, 0.14f, 0.18f), Snow, Matte);
            m.Ellipsoid(new Vector3(0.18f, 0.01f, -0.1f), new Vector3(0.16f, 0.11f, 0.15f), Snow, Matte);
            return m.Create("SnowPile", false);
        }

        /// <summary>Frame spruce (outside the field): two needle cones with snow, two sizes.</summary>
        public static Mesh BuildFrameTree(string name, float size)
        {
            var m = new SnowMesh();
            m.Cylinder(Vector3.zero, 0.14f * size, 0.3f * size, Trunk);
            m.Cone(new Vector3(0f, 0.25f * size, 0f), 0.8f * size, 1.0f * size, NeedlesDark);
            m.Torus(new Vector3(0f, 0.27f * size, 0f), 0.74f * size, 0.06f * size, Snow);
            m.Cone(new Vector3(0f, 0.85f * size, 0f), 0.56f * size, 0.95f * size, Needles);
            m.Cone(new Vector3(0f, 1.4f * size, 0f), 0.24f * size, 0.42f * size, Snow);
            return m.Create(name, false);
        }

        public static Mesh[] BuildFrameTrees() => new[] { BuildFrameTree("FrameSpruce_Small", 1.1f), BuildFrameTree("FrameSpruce_Tall", 1.45f) };

        /// <summary>
        /// Recolours a tall-grass bush mesh into a snow-capped conifer (D-140): the hue moves to dark needles, the top of every blade
        /// turns to snow; vertex alpha (sway weight) and geometry are kept.
        /// </summary>
        public static Mesh SnowBush(Mesh source, float maxHeight)
        {
            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + "_Snow";
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            mesh.GetVertices(vertices);
            mesh.GetColors(colors);
            for (int i = 0; i < colors.Count; i++)
            {
                Color srgb = colors[i].gamma;
                Color.RGBToHSV(srgb, out _, out float s, out float v);
                float t = Mathf.Clamp01(vertices[i].y / Mathf.Max(maxHeight, 0.01f));
                Color needles = Color.HSVToRGB(NeedleHue, s * NeedleSaturation, Mathf.Lerp(NeedleValueFoot, NeedleValueTop, t) * Mathf.Sqrt(v));
                Color c = Color.Lerp(needles, Snow, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SnowCapFrom, SnowCapFull, t)));
                Color linear = c.linear;
                linear.a = colors[i].a;
                colors[i] = linear;
            }
            mesh.SetColors(colors);
            return mesh;
        }

        private const float NeedleHue = 0.39f;
        private const float NeedleSaturation = 0.62f;
        private const float NeedleValueFoot = 0.22f;
        private const float NeedleValueTop = 0.5f;
        private const float SnowCapFrom = 0.72f;
        private const float SnowCapFull = 0.95f;

        /// <summary>A buffer with per-vertex gloss and the helpers above; colours are sRGB until <see cref="Create"/>.</summary>
        private sealed class SnowMesh
        {
            private readonly MeshBuilderBuffer _buffer = new MeshBuilderBuffer();
            private readonly List<float> _gloss = new List<float>();

            private void Track(float gloss)
            {
                while (_gloss.Count < _buffer.VertexCount) _gloss.Add(gloss);
            }

            public void Sphere(Vector3 centre, float radius, Color color, float gloss) =>
                Ellipsoid(centre, new Vector3(radius, radius, radius), color, gloss);

            public void Ellipsoid(Vector3 centre, Vector3 radii, Color color, float gloss)
            {
                ProcMesh.Ellipsoid(_buffer, Matrix4x4.Translate(centre), radii, radii.x < 0.1f ? SmallSegments : Segments, color, 0);
                Track(gloss);
            }

            public void Box(Vector3 centre, Vector3 halfExtents, float radius, Color color)
            {
                ProcMesh.RoundedBox(_buffer, Matrix4x4.Translate(centre), halfExtents, radius, ArcSteps, color, 0);
                Track(Matte);
            }

            public void Cylinder(Vector3 bottom, float radius, float height, Color color)
            {
                Vector2[] profile = { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius, height), new Vector2(0f, height) };
                Vector2[] normals = { Vector2.down, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.up };
                ProcMesh.Lathe(_buffer, Matrix4x4.Translate(bottom), profile, normals, Segments, color, 0);
                Track(Matte);
            }

            public void Cone(Vector3 bottom, float radius, float height, Color color)
            {
                Vector2 side = new Vector2(height, radius).normalized;
                Vector2[] profile = { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(0f, height) };
                Vector2[] normals = { Vector2.down, side, Vector2.up };
                ProcMesh.Lathe(_buffer, Matrix4x4.Translate(bottom), profile, normals, Segments + 4, color, 0);
                Track(Matte);
            }

            public void ConeAlong(Vector3 bottom, Vector3 axis, float radius, float length, Color color)
            {
                Vector2 side = new Vector2(length, radius).normalized;
                Vector2[] profile = { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(0f, length) };
                Vector2[] normals = { Vector2.down, side, Vector2.up };
                Matrix4x4 matrix = Matrix4x4.TRS(bottom, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one);
                ProcMesh.Lathe(_buffer, matrix, profile, normals, SmallSegments, color, 0);
                Track(Matte);
            }

            public void Torus(Vector3 centre, float ringRadius, float tubeRadius, Color color)
            {
                ProcMesh.Torus(_buffer, Matrix4x4.Translate(centre), ringRadius, tubeRadius, Segments + 6, SmallSegments, color, 0);
                Track(Matte);
            }

            /// <summary>Vertices of colour <paramref name="only"/> below <paramref name="height"/> blend towards <paramref name="shade"/>.</summary>
            public void ShadeTowards(float height, Color shade, Color only)
            {
                Color32 match = only;
                for (int i = 0; i < _buffer.VertexCount; i++)
                {
                    Color32 c = _buffer.Colors[i];
                    if (c.r != match.r || c.g != match.g || c.b != match.b) continue;
                    float t = 1f - Mathf.Clamp01(_buffer.Positions[i].y / height);
                    _buffer.Colors[i] = Color.Lerp(only, shade, t);
                }
            }

            /// <summary>Linear colours, gloss in alpha, uv0.x = 1; <paramref name="shadeFoot"/> darkens the vertices near the ground.</summary>
            public Mesh Create(string name, bool shadeFoot)
            {
                var mesh = new Mesh { name = name };
                _buffer.WriteTo(mesh);
                var colors = new List<Color>(_buffer.VertexCount);
                var uvs = new List<Vector2>(_buffer.VertexCount);
                for (int i = 0; i < _buffer.VertexCount; i++)
                {
                    Color c = _buffer.Colors[i];
                    if (shadeFoot && _buffer.Positions[i].y < FootHeight)
                        c = Color.Lerp(c * FootShade, c, _buffer.Positions[i].y / FootHeight);
                    Color linear = c.linear;
                    linear.a = i < _gloss.Count ? _gloss[i] : Matte;
                    colors.Add(linear);
                    uvs.Add(new Vector2(1f, 0f));
                }
                mesh.SetColors(colors);
                mesh.SetUVs(0, uvs);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
