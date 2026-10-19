using UnityEngine;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Procedural props of the snow theme (D-140, Map-SnowValley-LB §5 п.4), primitives only: spruce (4 cones + snow rims + baubles +
    /// a bulb garland + a star, D-142), gifts (box + ribbon cross + bow, 3 colours; big one 1.0), snowman (3 spheres + hat + scarf,
    /// 2 scarf colours), snow bricks (soft, cracked) and fort corners (bricks on a dark wooden plinth; with a lantern, D-142), drift,
    /// sled, flat snow pile, a lump at a wall foot and the Acorns spruce-hollow (Map-SnowValley §1.4 В2). The frame is <see cref="SnowFrameMeshes"/>, the bushes <see cref="SnowGroveMeshes"/>.
    /// Pivot at the bottom centre of a 1 × 1 cell; colours authored in sRGB and written linear; vertex alpha = gloss (matte 0,
    /// baubles and the star glossy); uv0.x = 1 everywhere (no texture detail, Arena/ToonEnv). No team colours (D-136).
    /// </summary>
    public static class SnowPropMeshes
    {
        private const float Matte = SnowMesh.Matte;
        private const float Glossy = SnowMesh.Glossy;

        internal static readonly Color Snow = new Color32(0xFB, 0xFC, 0xFE, 0xFF);
        internal static readonly Color SnowShade = new Color32(0xD2, 0xDE, 0xEC, 0xFF);
        internal static readonly Color SnowBlue = new Color32(0xE4, 0xEE, 0xF8, 0xFF);
        internal static readonly Color Needles = new Color32(0x2E, 0x5A, 0x3C, 0xFF);
        internal static readonly Color NeedlesDark = new Color32(0x22, 0x47, 0x2F, 0xFF);
        internal static readonly Color NeedlesLight = new Color32(0x3C, 0x70, 0x4C, 0xFF);
        internal static readonly Color Trunk = new Color32(0x6B, 0x44, 0x26, 0xFF);
        internal static readonly Color Gold = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        internal static readonly Color Pearl = new Color32(0xF5, 0xF2, 0xEA, 0xFF);
        internal static readonly Color Lilac = new Color32(0xA7, 0x7B, 0xD0, 0xFF);
        internal static readonly Color Apricot = new Color32(0xF0, 0xA0, 0x48, 0xFF);
        internal static readonly Color Mint = new Color32(0x6E, 0xCF, 0xB0, 0xFF);
        internal static readonly Color GiftGold = new Color32(0xE9, 0xB2, 0x3E, 0xFF);
        internal static readonly Color GiftLilac = new Color32(0x8E, 0x66, 0xC8, 0xFF);
        internal static readonly Color RibbonWhite = new Color32(0xF8, 0xF4, 0xEC, 0xFF);
        internal static readonly Color BigGiftBody = new Color32(0x6F, 0x4C, 0xB8, 0xFF);
        internal static readonly Color Coal = new Color32(0x2A, 0x26, 0x2E, 0xFF);
        internal static readonly Color Hat = new Color32(0x3A, 0x35, 0x40, 0xFF);
        internal static readonly Color Carrot = new Color32(0xF0, 0x8A, 0x3C, 0xFF);
        internal static readonly Color ScarfGreen = new Color32(0x3E, 0x9A, 0x5A, 0xFF);
        internal static readonly Color ScarfLilac = new Color32(0x9A, 0x6C, 0xC8, 0xFF);
        internal static readonly Color Brick = new Color32(0xEC, 0xF3, 0xFA, 0xFF);
        internal static readonly Color BrickSoft = new Color32(0xDD, 0xEA, 0xF6, 0xFF);
        internal static readonly Color BrickSeam = new Color32(0x8E, 0xA6, 0xC0, 0xFF);
        internal static readonly Color Crack = new Color32(0x7C, 0x92, 0xAE, 0xFF);
        internal static readonly Color Plinth = new Color32(0x5E, 0x40, 0x2A, 0xFF);
        internal static readonly Color PlinthLight = new Color32(0x7A, 0x56, 0x38, 0xFF);
        internal static readonly Color SledWood = new Color32(0xA2, 0x5E, 0x32, 0xFF);
        internal static readonly Color SledWoodDark = new Color32(0x7A, 0x44, 0x22, 0xFF);
        internal static readonly Color Runner = new Color32(0x4A, 0x46, 0x52, 0xFF);

        // D-142: the centre spruce is taller (user: the old one looked squat): 4 tiers up to ≈ 3.6, a spiral garland of warm bulbs,
        // a bigger star. Tiers: (bottom radius, bottom height, cone height).
        private static readonly Vector3[] TreeTiers =
        {
            new Vector3(1.48f, 0.34f, 1.35f), new Vector3(1.15f, 1.12f, 1.22f), new Vector3(0.84f, 1.86f, 1.1f), new Vector3(0.52f, 2.56f, 1.04f)
        };
        private const float TreeGarlandFrom = 0.5f;
        private const float TreeGarlandTo = 3.2f;
        private const float TreeGarlandTurns = 3.25f;
        private const float TreeGarlandStep = 0.21f;
        private const int TreeGarlandSamples = 600;
        private const float TreeBulbRadius = 0.075f;
        internal const int BulbSegments = 6;
        internal static readonly Color BulbWarm = new Color32(0xFF, 0xD2, 0x4A, 0xFF);
        internal static readonly Color BulbWhite = new Color32(0xFF, 0xF4, 0xD2, 0xFF);
        internal static readonly Color BulbPink = new Color32(0xFF, 0x86, 0xB4, 0xFF);
        internal static readonly Color BulbMint = new Color32(0x78, 0xE6, 0xC4, 0xFF);
        internal static readonly Color BulbLilac = new Color32(0xB8, 0x92, 0xF0, 0xFF);

        /// <summary>Bulb colour cycle (D-142): mostly warm yellow / white, a little pink, mint and lilac (no team colour dominates).</summary>
        internal static readonly Color[] BulbCycle =
            { BulbWarm, BulbWhite, BulbWarm, BulbPink, BulbWarm, BulbWhite, BulbWarm, BulbMint, BulbWarm, BulbWhite, BulbWarm, BulbLilac };

        /// <summary>Spruce of the centre plus (Ø ≈ 3 cells, h ≈ 3.6): trunk, four needle cones with snow rims, baubles, a spiral garland
        /// of glossy bulbs and a star.</summary>
        public static Mesh BuildTree()
        {
            var m = new SnowMesh();
            m.Cylinder(Vector3.zero, 0.26f, 0.46f, Trunk);
            Color[] tones = { NeedlesDark, Needles, Needles, NeedlesLight };
            for (int t = 0; t < TreeTiers.Length; t++)
            {
                Vector3 tier = TreeTiers[t];
                m.Cone(new Vector3(0f, tier.y, 0f), tier.x, tier.z, tones[t]);
                m.Torus(new Vector3(0f, tier.y + 0.03f, 0f), tier.x * 0.93f, 0.08f, Snow);
                m.Cone(new Vector3(0f, tier.y + tier.z * 0.62f, 0f), tier.x * 0.33f, tier.z * 0.38f, Snow); // snow on the tip
            }
            Color[] baubles = { Gold, Pearl, Apricot, Lilac };
            for (int t = 0; t < TreeTiers.Length; t++)
            {
                Vector3 tier = TreeTiers[t];
                int count = 7 - t;
                for (int k = 0; k < count; k++)
                {
                    float a = (k + t * 0.5f) * Mathf.PI * 2f / count;
                    const float h = 0.3f;
                    float r = tier.x * (1f - h) * 0.98f;
                    var p = new Vector3(Mathf.Cos(a) * r, tier.y + tier.z * h, Mathf.Sin(a) * r);
                    m.Sphere(p, 0.13f, baubles[(k + t) % baubles.Length], Glossy);
                }
            }
            // Spiral garland: bulbs on the needle surface from the bottom tier up, evenly spaced along the spiral.
            float run = 0f;
            Vector3 previous = TreeSurface(TreeGarlandFrom, 0f);
            int bulb = 0;
            for (int i = 1; i <= TreeGarlandSamples; i++)
            {
                float f = i / (float)TreeGarlandSamples;
                Vector3 p = TreeSurface(Mathf.Lerp(TreeGarlandFrom, TreeGarlandTo, f), f * TreeGarlandTurns * Mathf.PI * 2f);
                run += Vector3.Distance(previous, p);
                previous = p;
                if (run < TreeGarlandStep) continue;
                run = 0f;
                m.Ellipsoid(p, Vector3.one * TreeBulbRadius, BulbCycle[bulb++ % BulbCycle.Length], Glossy, BulbSegments);
            }
            Vector3 last = TreeTiers[TreeTiers.Length - 1];
            var star = new Vector3(0f, last.y + last.z + 0.08f, 0f);
            m.Sphere(star, 0.24f, Gold, Glossy);
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f + Mathf.PI * 0.5f;
                m.ConeAlong(star, new Vector3(Mathf.Cos(a), Mathf.Sin(a), -0.15f).normalized, 0.11f, 0.42f, Gold);
            }
            return m.Create("SnowTree", false);
        }

        // Map-SnowValley §1.4 В2 / §4: the Acorns spruce-hollow — ≤ 1 cell at the base, ≈ 1.6 high (does not hide a fighter in the
        // pickup zone), on a snow mound with two burrows (the acorns roll out of them; ±Z so both team cameras see one). Tiers as
        // TreeTiers: (bottom radius, bottom height, cone height).
        private static readonly Vector3[] HollowTiers =
        {
            new Vector3(0.47f, 0.26f, 0.6f), new Vector3(0.37f, 0.6f, 0.52f), new Vector3(0.26f, 0.92f, 0.46f)
        };
        private static readonly Vector3 HollowMound = new Vector3(0.5f, 0.2f, 0.5f);
        private const float HollowBaubleRadius = 0.075f;
        private const float HollowBulbRadius = 0.05f;
        private const float HollowGarlandFrom = 0.34f;
        private const float HollowGarlandTo = 1.22f;
        private const float HollowGarlandTurns = 1.75f;
        private const int HollowGarlandBulbs = 8;
        private const int HollowBaublesBottom = 3;
        // Segments: one mesh ≤ 1k triangles (§4, weak phones).
        private const int HollowMoundSegments = 8;
        private const int HollowConeSegments = 12;
        private const int HollowRimSegments = 8;
        private const int HollowBaubleSegments = 6;
        private const int HollowSmallSegments = 4;
        private static readonly Color Burrow = new Color32(0x4A, 0x34, 0x24, 0xFF);

        /// <summary>The Acorns hollow of a snow map: a small decorated spruce on a snow mound with burrows at its foot, baubles
        /// gold / white / apricot / lilac (no team red or blue), a spiral of warm bulbs and a star (≤ 1 cell, h ≈ 1.6).</summary>
        public static Mesh BuildHollowSpruce()
        {
            var m = new SnowMesh();
            m.Ellipsoid(Vector3.zero, HollowMound, SnowBlue, Matte, HollowMoundSegments);
            for (int s = -1; s <= 1; s += 2)
                m.Ellipsoid(new Vector3(0f, 0.05f, s * (HollowMound.z - 0.06f)), new Vector3(0.17f, 0.1f, 0.06f), Burrow, Matte, HollowSmallSegments);
            Color[] tones = { NeedlesDark, Needles, NeedlesLight };
            for (int t = 0; t < HollowTiers.Length; t++)
            {
                Vector3 tier = HollowTiers[t];
                m.Cone(new Vector3(0f, tier.y, 0f), tier.x, tier.z, tones[t], HollowConeSegments);
                if (t == HollowTiers.Length - 1) continue; // the star sits on the top tier
                m.Ellipsoid(new Vector3(0f, tier.y + 0.02f, 0f), new Vector3(tier.x * 0.98f, 0.035f, tier.x * 0.98f), Snow, Matte, HollowRimSegments);
                m.Cone(new Vector3(0f, tier.y + tier.z * 0.64f, 0f), tier.x * 0.32f, tier.z * 0.36f, Snow, SnowMesh.SmallSegments);
            }
            Color[] baubles = { Gold, Pearl, Apricot, Lilac };
            for (int t = 0; t < HollowTiers.Length; t++)
            {
                Vector3 tier = HollowTiers[t];
                int count = HollowBaublesBottom - t;
                for (int k = 0; k < count; k++)
                {
                    float a = (k + t * 0.5f) * Mathf.PI * 2f / count;
                    const float h = 0.28f;
                    float r = tier.x * (1f - h);
                    var p = new Vector3(Mathf.Cos(a) * r, tier.y + tier.z * h, Mathf.Sin(a) * r);
                    m.Ellipsoid(p, Vector3.one * HollowBaubleRadius, baubles[(k + t) % baubles.Length], Glossy, HollowBaubleSegments);
                }
            }
            for (int i = 0; i < HollowGarlandBulbs; i++)
            {
                float f = (i + 0.5f) / HollowGarlandBulbs;
                float y = Mathf.Lerp(HollowGarlandFrom, HollowGarlandTo, f);
                float angle = f * HollowGarlandTurns * Mathf.PI * 2f;
                float radius = 0f;
                foreach (Vector3 tier in HollowTiers)
                {
                    float u = (y - tier.y) / tier.z;
                    if (u >= 0f && u <= 1f) radius = Mathf.Max(radius, tier.x * (1f - u));
                }
                radius += HollowBulbRadius * 0.6f;
                m.Ellipsoid(new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius), Vector3.one * HollowBulbRadius,
                    BulbCycle[i % BulbCycle.Length], Glossy, HollowSmallSegments);
            }
            Vector3 last = HollowTiers[HollowTiers.Length - 1];
            var star = new Vector3(0f, last.y + last.z + 0.06f, 0f);
            m.Ellipsoid(star, Vector3.one * 0.1f, Gold, Glossy, HollowBaubleSegments);
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f + Mathf.PI * 0.5f;
                m.ConeAlong(star, new Vector3(Mathf.Cos(a), Mathf.Sin(a), -0.15f).normalized, 0.05f, 0.18f, Gold);
            }
            return m.Create("SnowHollowSpruce", false);
        }

        /// <summary>A point just outside the needle surface of the centre spruce at height <paramref name="y"/> and angle
        /// <paramref name="angle"/> (the widest tier there).</summary>
        private static Vector3 TreeSurface(float y, float angle)
        {
            float radius = 0f;
            foreach (Vector3 tier in TreeTiers)
            {
                float t = (y - tier.y) / tier.z;
                if (t < 0f || t > 1f) continue;
                radius = Mathf.Max(radius, tier.x * (1f - t));
            }
            radius += TreeBulbRadius * 0.6f;
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
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
            AddFortCorner(m);
            return m.Create("SnowFortCorner", false);
        }

        private static void AddFortCorner(SnowMesh m)
        {
            m.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.49f, 0.14f, 0.49f), 0.04f, Plinth);
            for (int k = 0; k < 4; k++)
                m.Box(new Vector3(-0.36f + k * 0.24f, 0.14f, -0.492f), new Vector3(0.005f, 0.13f, 0.005f), 0.002f, PlinthLight);
            AddBrickRows(m, 0.28f, 0.36f, Brick);
            m.Box(new Vector3(0f, 1.02f, 0f), new Vector3(0.44f, 0.04f, 0.44f), 0.04f, Snow); // snow cap
        }

        /// <summary>Fort corner / pier with a lantern (wall 'L', D-142): the corner and a wooden post with a warm glass lantern under a
        /// snowy roof.</summary>
        public static Mesh BuildFortLantern()
        {
            var m = new SnowMesh();
            AddFortCorner(m);
            const float Base = 1.06f, Post = 0.62f;
            m.Box(new Vector3(0f, Base + 0.04f, 0f), new Vector3(0.12f, 0.04f, 0.12f), 0.02f, Plinth);
            m.Cylinder(new Vector3(0f, Base, 0f), 0.045f, Post, Plinth);
            float y = Base + Post;
            m.Box(new Vector3(0f, y + 0.02f, 0f), new Vector3(0.13f, 0.025f, 0.13f), 0.01f, Plinth);
            m.Box(new Vector3(0f, y + 0.17f, 0f), new Vector3(0.1f, 0.13f, 0.1f), 0.03f, BulbWarm);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    m.Box(new Vector3(sx * 0.105f, y + 0.17f, sz * 0.105f), new Vector3(0.018f, 0.14f, 0.018f), 0.006f, Hat);
            m.Cone(new Vector3(0f, y + 0.3f, 0f), 0.19f, 0.16f, Hat, SnowMesh.SmallSegments);
            m.Cone(new Vector3(0f, y + 0.33f, 0f), 0.15f, 0.14f, Snow, SnowMesh.SmallSegments);
            m.Sphere(new Vector3(0f, y + 0.5f, 0f), 0.035f, Gold, Glossy);
            return m.Create("SnowFortLantern", false);
        }

        /// <summary>Small snow lump at the foot of a wall (D-142, view only, walkable): two soft mounds about 0.15 high.</summary>
        public static Mesh BuildWallLump()
        {
            var m = new SnowMesh();
            m.Ellipsoid(Vector3.zero, new Vector3(0.3f, 0.15f, 0.2f), Snow, Matte, SnowMesh.SmallSegments);
            m.Ellipsoid(new Vector3(0.2f, 0f, 0.04f), new Vector3(0.17f, 0.1f, 0.14f), SnowBlue, Matte, SnowMesh.SmallSegments);
            m.ShadeTowards(0.12f, SnowShade, SnowBlue);
            return m.Create("SnowWallLump", false);
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
    }
}
