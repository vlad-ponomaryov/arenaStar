using UnityEngine;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Frame of the snow arena (D-142, user feedback 1 on Snow Valley: no spruces outside the field, a snow-brick wall with garlands):
    /// a wall segment (snow bricks in running bond on a wooden plinth, a snow cap), a wooden post, a sagging garland of bulbs from post
    /// to post, and what stands behind the wall — a log cabin with a warm window, a woodpile, snow mounds. Primitives only, built like
    /// <see cref="SnowPropMeshes"/> (pivot at the bottom centre; the wall and the garland run along +X, fronts face −Z).
    /// </summary>
    public static class SnowFrameMeshes
    {
        public const float WallHeight = 0.92f;
        public const float WallHalfDepth = 0.3f;
        public const float PostHeight = 1.42f;
        public const float GarlandHeight = 1.3f;
        private const float GarlandSag = 0.3f;
        private const float GarlandBulbStep = 0.24f;
        private const float GarlandBulbRadius = 0.075f;
        private const int GarlandWireSteps = 12;

        private static readonly Color Wire = new Color32(0x2E, 0x4A, 0x36, 0xFF);
        private static readonly Color Log = new Color32(0x9A, 0x5E, 0x36, 0xFF);
        private static readonly Color LogDark = new Color32(0x74, 0x44, 0x26, 0xFF);
        private static readonly Color LogEnd = new Color32(0xE2, 0xB8, 0x80, 0xFF);
        private static readonly Color Roof = new Color32(0x8A, 0x3E, 0x3A, 0xFF);
        private static readonly Color Window = new Color32(0xFF, 0xC8, 0x50, 0xFF);
        private static readonly Color Chimney = new Color32(0x8C, 0x86, 0x90, 0xFF);

        /// <summary>One wall segment of <paramref name="length"/> along X: plinth, two rows of snow bricks (the upper one shifted by half
        /// a brick — neighbouring segments continue the bond), a soft snow cap with a lump.</summary>
        public static Mesh BuildWall(float length)
        {
            var m = new SnowMesh();
            float half = length * 0.5f;
            const float PlinthTop = 0.24f, Row = 0.3f, Gap = 0.022f;
            m.Box(new Vector3(0f, PlinthTop * 0.5f, 0f), new Vector3(half, PlinthTop * 0.5f, WallHalfDepth + 0.02f), 0.03f, SnowPropMeshes.Plinth);
            m.Box(new Vector3(0f, PlinthTop * 0.5f, -WallHalfDepth - 0.021f), new Vector3(half, 0.008f, 0.004f), 0.002f, SnowPropMeshes.PlinthLight);
            // Dark seam core, below the brick tops.
            m.Box(new Vector3(0f, PlinthTop + Row - 0.02f, 0f), new Vector3(half, Row - 0.04f, WallHalfDepth - 0.03f), 0.02f, SnowPropMeshes.BrickSeam);
            float brick = length * 0.5f;
            for (int row = 0; row < 2; row++)
            {
                float y = PlinthTop + Row * (row + 0.5f);
                float shift = row == 0 ? 0f : brick * 0.5f;
                for (float x = -half - shift; x < half - 1e-4f; x += brick)
                {
                    float x0 = Mathf.Max(x, -half), x1 = Mathf.Min(x + brick, half);
                    if (x1 - x0 < 1e-3f) continue;
                    // Halves at the segment ends meet the neighbour's halves: a seam only where a whole brick would have one.
                    float inset0 = x0 > -half + 1e-4f ? Gap : 0f, inset1 = x1 < half - 1e-4f ? Gap : 0f;
                    var centre = new Vector3((x0 + inset0 + x1 - inset1) * 0.5f, y, 0f);
                    var size = new Vector3((x1 - inset1 - x0 - inset0) * 0.5f, Row * 0.5f - Gap, WallHalfDepth);
                    m.Box(centre, size, 0.06f, row == 0 ? SnowPropMeshes.BrickSoft : SnowPropMeshes.Brick);
                }
            }
            float top = PlinthTop + Row * 2f;
            m.Box(new Vector3(0f, top + 0.02f, 0f), new Vector3(half, 0.05f, WallHalfDepth + 0.03f), 0.05f, SnowPropMeshes.Snow);
            m.Ellipsoid(new Vector3(length * 0.18f, top + 0.05f, 0.02f), new Vector3(0.22f, 0.08f, 0.2f), SnowPropMeshes.Snow, SnowMesh.Matte,
                SnowMesh.SmallSegments);
            return m.Create("FrameWall", true);
        }

        /// <summary>Wooden post (taller than the wall) with a snow cap and a gold knob; garlands hang from its top.</summary>
        public static Mesh BuildPost()
        {
            var m = new SnowMesh();
            m.Box(new Vector3(0f, PostHeight * 0.5f, 0f), new Vector3(0.1f, PostHeight * 0.5f, 0.1f), 0.025f, SnowPropMeshes.SledWood);
            m.Box(new Vector3(0f, PostHeight + 0.02f, 0f), new Vector3(0.13f, 0.045f, 0.13f), 0.04f, SnowPropMeshes.Snow);
            m.Sphere(new Vector3(0f, PostHeight + 0.12f, 0f), 0.065f, SnowPropMeshes.Gold, SnowMesh.Glossy);
            return m.Create("FramePost", true);
        }

        /// <summary>Garland from x = 0 to x = <paramref name="span"/> (post to post): a sagging dark wire and glossy bulbs on it, in front
        /// of the wall's field side.</summary>
        public static Mesh BuildGarland(float span)
        {
            var m = new SnowMesh();
            float z = -WallHalfDepth - 0.05f;
            for (int i = 0; i < GarlandWireSteps; i++)
                m.Segment(GarlandPoint(span, i / (float)GarlandWireSteps, z), GarlandPoint(span, (i + 1) / (float)GarlandWireSteps, z), 0.012f, Wire);
            int bulbs = Mathf.Max(1, Mathf.RoundToInt(span / GarlandBulbStep));
            for (int k = 0; k < bulbs; k++)
            {
                Vector3 p = GarlandPoint(span, (k + 0.5f) / bulbs, z) + Vector3.down * 0.05f;
                m.Ellipsoid(p, new Vector3(GarlandBulbRadius, GarlandBulbRadius * 1.25f, GarlandBulbRadius), SnowPropMeshes.BulbCycle[k % SnowPropMeshes.BulbCycle.Length],
                    SnowMesh.Glossy, SnowPropMeshes.BulbSegments);
            }
            return m.Create("FrameGarland", false);
        }

        private static Vector3 GarlandPoint(float span, float t, float z)
        {
            float u = t * 2f - 1f;
            return new Vector3(span * t, GarlandHeight - GarlandSag * (1f - u * u), z);
        }

        /// <summary>Log cabin (≈ 2.2 × 1.7, h ≈ 2.4) facing −Z: log walls, a snowy gable roof, a warm window, a door, a chimney.</summary>
        public static Mesh BuildCabin()
        {
            var m = new SnowMesh();
            const float HalfX = 1.1f, HalfZ = 0.85f, Height = 1.25f, LogR = 0.12f;
            for (int i = 0; i * LogR * 2f < Height; i++)
            {
                float y = LogR + i * LogR * 2f;
                Color c = i % 2 == 0 ? Log : LogDark;
                m.CylinderAlong(new Vector3(-HalfX - 0.1f, y, -HalfZ), Vector3.right, LogR, HalfX * 2f + 0.2f, c, SnowMesh.SmallSegments);
                m.CylinderAlong(new Vector3(-HalfX - 0.1f, y, HalfZ), Vector3.right, LogR, HalfX * 2f + 0.2f, c, SnowMesh.SmallSegments);
                m.CylinderAlong(new Vector3(-HalfX, y, -HalfZ - 0.1f), Vector3.forward, LogR, HalfZ * 2f + 0.2f, c, SnowMesh.SmallSegments);
                m.CylinderAlong(new Vector3(HalfX, y, -HalfZ - 0.1f), Vector3.forward, LogR, HalfZ * 2f + 0.2f, c, SnowMesh.SmallSegments);
            }
            m.Box(new Vector3(0f, Height * 0.5f, 0f), new Vector3(HalfX - 0.02f, Height * 0.5f, HalfZ - 0.02f), 0.02f, LogDark);
            // Gable roof along X: two slabs, snow on top, ridge snow.
            const float RoofRise = 0.85f, Overhang = 0.25f;
            float slope = Mathf.Atan2(RoofRise, HalfZ + Overhang) * Mathf.Rad2Deg;
            float slabHalf = Mathf.Sqrt(RoofRise * RoofRise + (HalfZ + Overhang) * (HalfZ + Overhang)) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion tilt = Quaternion.Euler(s * slope, 0f, 0f);
                var centre = new Vector3(0f, Height + RoofRise * 0.5f, -s * (HalfZ + Overhang) * 0.5f);
                m.Box(Matrix4x4.TRS(centre, tilt, Vector3.one), new Vector3(HalfX + Overhang, 0.06f, slabHalf), 0.03f, Roof);
                m.Box(Matrix4x4.TRS(centre + tilt * Vector3.up * 0.08f, tilt, Vector3.one), new Vector3(HalfX + Overhang - 0.04f, 0.06f, slabHalf - 0.03f), 0.06f,
                    SnowPropMeshes.Snow);
            }
            m.CylinderAlong(new Vector3(-HalfX - Overhang, Height + RoofRise + 0.06f, 0f), Vector3.right, 0.1f, (HalfX + Overhang) * 2f, SnowPropMeshes.Snow,
                SnowMesh.SmallSegments);
            // Front (−Z): a glowing window with a cross and a door.
            float front = -HalfZ - LogR - 0.01f;
            m.Box(new Vector3(-0.45f, 0.72f, front), new Vector3(0.24f, 0.2f, 0.02f), 0.02f, Window);
            m.Box(new Vector3(-0.45f, 0.72f, front - 0.015f), new Vector3(0.02f, 0.2f, 0.01f), 0.005f, LogDark);
            m.Box(new Vector3(-0.45f, 0.72f, front - 0.015f), new Vector3(0.24f, 0.02f, 0.01f), 0.005f, LogDark);
            m.Box(new Vector3(-0.45f, 0.5f, front - 0.04f), new Vector3(0.3f, 0.035f, 0.05f), 0.02f, SnowPropMeshes.Snow);
            m.Box(new Vector3(0.45f, 0.5f, front), new Vector3(0.22f, 0.5f, 0.03f), 0.03f, Roof);
            m.Sphere(new Vector3(0.6f, 0.5f, front - 0.04f), 0.035f, SnowPropMeshes.Gold, SnowMesh.Glossy);
            m.Box(new Vector3(0.65f, Height + RoofRise * 0.7f, 0.3f), new Vector3(0.14f, 0.45f, 0.14f), 0.03f, Chimney);
            m.Box(new Vector3(0.65f, Height + RoofRise * 0.7f + 0.47f, 0.3f), new Vector3(0.17f, 0.05f, 0.17f), 0.04f, SnowPropMeshes.Snow);
            m.Ellipsoid(new Vector3(0f, 0f, front - 0.1f), new Vector3(HalfX + 0.2f, 0.16f, 0.25f), SnowPropMeshes.SnowBlue, SnowMesh.Matte);
            return m.Create("FrameCabin", true);
        }

        /// <summary>Woodpile (≈ 1.4 × 0.6): stacked logs with light cut ends towards −Z and snow on top.</summary>
        public static Mesh BuildWoodpile()
        {
            var m = new SnowMesh();
            const float R = 0.13f, Depth = 0.6f;
            int[] perRow = { 5, 4, 3 };
            for (int row = 0; row < perRow.Length; row++)
            {
                float y = R + row * R * 1.72f;
                for (int k = 0; k < perRow[row]; k++)
                {
                    float x = (k - (perRow[row] - 1) * 0.5f) * R * 2.02f;
                    m.CylinderAlong(new Vector3(x, y, Depth * 0.5f), Vector3.back, R, Depth, (k + row) % 2 == 0 ? Log : LogDark, SnowMesh.SmallSegments);
                    m.CylinderAlong(new Vector3(x, y, -Depth * 0.5f), Vector3.back, R * 0.78f, 0.012f, LogEnd, SnowMesh.SmallSegments);
                }
            }
            m.Ellipsoid(new Vector3(0f, R + 2f * R * 1.72f + 0.08f, 0f), new Vector3(0.36f, 0.09f, Depth * 0.5f), SnowPropMeshes.Snow, SnowMesh.Matte);
            m.Ellipsoid(Vector3.zero, new Vector3(0.85f, 0.12f, 0.45f), SnowPropMeshes.SnowBlue, SnowMesh.Matte);
            return m.Create("FrameWoodpile", true);
        }

        /// <summary>Snow mound behind the wall (two soft lumps).</summary>
        public static Mesh BuildMound()
        {
            var m = new SnowMesh();
            m.Ellipsoid(Vector3.zero, new Vector3(0.9f, 0.55f, 0.7f), SnowPropMeshes.SnowBlue, SnowMesh.Matte);
            m.Ellipsoid(new Vector3(0.55f, 0f, -0.2f), new Vector3(0.5f, 0.35f, 0.45f), SnowPropMeshes.Snow, SnowMesh.Matte);
            m.ShadeTowards(0.3f, SnowPropMeshes.SnowShade, SnowPropMeshes.Snow);
            return m.Create("FrameMound", false);
        }
    }
}
