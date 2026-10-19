using System;
using System.IO;
using Arena.Editor.Setup;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Procedural tileable textures of the environment (A.3b, D-125), baked in the editor into PNGs under
    /// <see cref="TargetFolder"/> (deterministic: the same seed gives the same bytes): the stone slabs with moss of the wall tops,
    /// the leaf detail of bushes and the ripple map of water. Values are "detail around 1" — the shaders divide by the mean
    /// luminance (returned) and the vertex colours keep the palette (Art-Spec §1.3).
    /// </summary>
    public static class EnvTextureBaker
    {
        public const string TargetFolder = ViewAssetBuilder.TexturesFolder + "/Env";
        public const string WallTopPath = TargetFolder + "/WallTop.png";
        public const string LeavesPath = TargetFolder + "/Leaves.png";
        public const string RipplePath = TargetFolder + "/Ripple.png";

        // Wall top: 256 px = 2 × 2 cells (one cell = 128 px), 3–5 slabs per cell, grout along the cell borders too.
        public const int WallTopSize = 256;
        public const int WallTopCellsPerTile = 2;
        private const int WallTopSeed = 1931;
        private const float GroutPx = 1.6f;
        private const float PillowPx = 11f;
        private static readonly Color Grout = new Color(0.70f, 0.66f, 0.60f, 1f);
        private const float PillowEdge = 0.83f;
        private const float SlabVariation = 0.06f;
        private const float SpeckleStrength = 0.035f;

        // Sand floor (A.3d, D-128): 512 px = 8 × 8 cells; calm warm sand — slabs of 2 × 2 cells in running bond with a faint seam
        // (≈ 4–5 %), low-frequency mottling (± 4 %), a fine grain and a few hairline cracks. Absolute sRGB colours (not detail ≈ 1).
        public const string SandPath = TargetFolder + "/Sand.png";
        public const int SandSize = 512;
        public const int SandCellsPerTile = 8;
        private const int SandSeed = 2718;
        private const int SandSlabCells = 2;
        private static readonly Color SandBase = new Color32(0xF6, 0xCF, 0x8A, 0xFF);
        private static readonly Color SandWarm = new Color32(0xF0, 0xC0, 0x7C, 0xFF);
        private const float SandSeamDarken = 0.045f;
        private const float SandSeamPx = 1.6f;
        private const float SandSeamSoftPx = 2.5f;
        private const float SandSlabVariation = 0.02f;
        private const float SandMottle = 0.04f;
        private const float SandWarmMottle = 0.35f;
        private const float SandGrain = 0.015f;
        private const int SandCracks = 3;
        private const int SandCrackSteps = 16;
        private const float SandCrackStepPx = 2.2f;
        private const float SandCrackDarken = 0.16f;

        // Leaves: 128 px, overlapping leaf ellipses lighter at the top, a darker vein edge.
        public const int LeavesSize = 128;
        private const int LeavesSeed = 77;
        private const int LeafCount = 150;

        // Ripple: 128 px, R = soft lines along the cell edges of a periodic Voronoi, G = smooth noise (foam wobble).
        public const int RippleSize = 128;
        private const int RippleSeed = 4242;
        private const int RippleCells = 9;
        private const float RippleLinePx = 4.5f;

        public static (Texture2D texture, float meanLuminance) BakeWallTop()
        {
            int n = WallTopSize, cellPx = n / WallTopCellsPerTile;
            var rng = new System.Random(WallTopSeed);
            // Seeds per cell (local px), slab brightness per seed.
            int cells = WallTopCellsPerTile * WallTopCellsPerTile;
            var seeds = new Vector2[cells][];
            var tints = new float[cells][];
            for (int k = 0; k < cells; k++)
            {
                int count = 3 + rng.Next(3);
                seeds[k] = new Vector2[count];
                tints[k] = new float[count];
                for (int s = 0; s < count; s++)
                {
                    seeds[k][s] = new Vector2(Range(rng, 0.12f, 0.88f) * cellPx, Range(rng, 0.12f, 0.88f) * cellPx);
                    tints[k][s] = 1f + Range(rng, -SlabVariation, SlabVariation);
                }
            }
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int cx = x / cellPx, cy = y / cellPx, k = cy * WallTopCellsPerTile + cx;
                    var local = new Vector2(x - cx * cellPx + 0.5f, y - cy * cellPx + 0.5f);
                    float f1 = float.MaxValue, f2 = float.MaxValue;
                    int nearest = 0;
                    for (int s = 0; s < seeds[k].Length; s++)
                    {
                        float d = Vector2.Distance(local, seeds[k][s]);
                        if (d < f1) { f2 = f1; f1 = d; nearest = s; }
                        else if (d < f2) f2 = d;
                    }
                    float edge = (f2 - f1) * 0.5f;
                    float border = Mathf.Min(Mathf.Min(local.x, cellPx - local.x), Mathf.Min(local.y, cellPx - local.y));
                    edge = Mathf.Min(edge, border);

                    float speckle = (Hash01(x, y, WallTopSeed) - 0.5f) * 2f * SpeckleStrength;
                    float pillow = Mathf.Lerp(PillowEdge, 1f, Smoothstep(GroutPx, PillowPx, edge));
                    float v = pillow * tints[k][nearest] + speckle;
                    Color stone = new Color(v, v * 0.995f, v * 0.985f, 1f);
                    // A.3d: no moss (it read as camouflage stains); the carved leaf relief of the top comes with A.3f.
                    px[y * n + x] = edge < GroutPx ? Color.Lerp(Grout, stone, Mathf.Clamp01(edge / GroutPx - 0.5f)) : stone;
                }
            }
            return Save(WallTopPath, px, n, true);
        }

        /// <summary>The sand floor tile (A.3d): see the constants above. Returns the texture and its linear mean luminance.</summary>
        public static (Texture2D texture, float meanLuminance) BakeSand()
        {
            int n = SandSize, cellPx = n / SandCellsPerTile, slabPx = cellPx * SandSlabCells, slabs = n / slabPx;
            var rng = new System.Random(SandSeed);
            var slabTint = new float[slabs * slabs];
            for (int i = 0; i < slabTint.Length; i++) slabTint[i] = 1f + Range(rng, -SandSlabVariation, SandSlabVariation);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                int row = y / slabPx;
                // Running bond: every other row of slabs is shifted by one cell.
                int shift = (row & 1) * cellPx;
                for (int x = 0; x < n; x++)
                {
                    int sx = ((x + shift) % n) / slabPx;
                    float lx = (x + shift) % slabPx + 0.5f, ly = y % slabPx + 0.5f;
                    float edge = Mathf.Min(Mathf.Min(lx, slabPx - lx), Mathf.Min(ly, slabPx - ly));
                    float seam = 1f - Smoothstep(SandSeamPx, SandSeamPx + SandSeamSoftPx, edge);
                    float mottle = Fbm(x, y, n, 3, SandSeed + 1);
                    float warm = Smoothstep(0.35f, 0.75f, Fbm(x, y, n, 2, SandSeed + 2)) * SandWarmMottle;
                    Color c = Color.Lerp(SandBase, SandWarm, warm);
                    float k = slabTint[row * slabs + sx] * (1f + (mottle - 0.5f) * 2f * SandMottle)
                              * (1f + (Hash01(x, y, SandSeed) - 0.5f) * 2f * SandGrain) * (1f - SandSeamDarken * seam);
                    px[y * n + x] = new Color(c.r * k, c.g * k, c.b * k, 1f);
                }
            }
            // A few hairline cracks: short random walks that wrap around the tile.
            for (int crack = 0; crack < SandCracks; crack++)
            {
                float cx = Range(rng, 0f, n), cy = Range(rng, 0f, n), dir = Range(rng, 0f, Mathf.PI * 2f);
                for (int step = 0; step < SandCrackSteps; step++)
                {
                    dir += Range(rng, -0.7f, 0.7f);
                    cx += Mathf.Cos(dir) * SandCrackStepPx;
                    cy += Mathf.Sin(dir) * SandCrackStepPx;
                    float fade = 1f - step / (float)SandCrackSteps;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = Mod((int)cx + dx, n), y = Mod((int)cy + dy, n);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float k = 1f - SandCrackDarken * fade * Mathf.Clamp01(1.2f - d * 0.6f);
                        Color c = px[y * n + x];
                        px[y * n + x] = new Color(c.r * k, c.g * k * 0.98f, c.b * k * 0.95f, 1f);
                    }
                }
            }
            return Save(SandPath, px, n, true);
        }

        public static (Texture2D texture, float meanLuminance) BakeLeaves()
        {
            int n = LeavesSize;
            var rng = new System.Random(LeavesSeed);
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.9f, 0.9f, 0.9f, 1f);
            for (int l = 0; l < LeafCount; l++)
            {
                float cx = Range(rng, 0f, n), cy = Range(rng, 0f, n);
                float a = Range(rng, 6f, 10f), b = Range(rng, 3f, 4.6f);
                float angle = Range(rng, -0.9f, 0.9f) + (rng.Next(2) == 0 ? 0f : Mathf.PI); // roughly horizontal leaves
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                float tint = Range(rng, 0.94f, 1.08f);
                bool yellow = rng.Next(5) == 0;
                int r = Mathf.CeilToInt(a) + 1;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float u = (dx * ca + dy * sa) / a, w = (-dx * sa + dy * ca) / b;
                        float d = u * u + w * w;
                        if (d > 1f) continue;
                        // Lighter towards the leaf's upper side (texture +y), a dark rim at the edge.
                        float up = Mathf.Clamp01(0.5f + 0.5f * (dy / (float)r));
                        float v = (0.92f + 0.22f * up) * tint;
                        if (d > 0.72f) v *= 0.78f;
                        int x = ((int)cx + dx) & (n - 1), y = ((int)cy + dy) & (n - 1);
                        px[y * n + x] = yellow ? new Color(v * 1.06f, v * 1.06f, v * 0.86f, 1f) : new Color(v, v, v, 1f);
                    }
                }
            }
            return Save(LeavesPath, px, n, true);
        }

        public static (Texture2D texture, float meanLuminance) BakeRipple()
        {
            int n = RippleSize;
            var rng = new System.Random(RippleSeed);
            var seeds = new Vector2[RippleCells];
            for (int i = 0; i < seeds.Length; i++) seeds[i] = new Vector2(Range(rng, 0f, n), Range(rng, 0f, n));
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float f1 = float.MaxValue, f2 = float.MaxValue;
                    for (int i = 0; i < seeds.Length; i++)
                    {
                        float d = WrapDistance(p, seeds[i], n);
                        if (d < f1) { f2 = f1; f1 = d; }
                        else if (d < f2) f2 = d;
                    }
                    float edge = (f2 - f1) * 0.5f;
                    float line = 1f - Smoothstep(0f, RippleLinePx, edge);
                    float noise = Fbm(x, y, n, 4, RippleSeed + 3);
                    px[y * n + x] = new Color(line, noise, 0f, 1f);
                }
            }
            return Save(RipplePath, px, n, false);
        }

        /// <summary>Periodic fBm in [0, 1]: value noise with lattice period <paramref name="basePeriod"/> over <paramref name="size"/> px, 3 octaves.</summary>
        public static float Fbm(int x, int y, int size, int basePeriod, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = basePeriod;
            for (int o = 0; o < 3; o++)
            {
                sum += amp * ValueNoise(x * period / (float)size, y * period / (float)size, period, seed + o * 101);
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash01(Mod(x0, period), Mod(y0, period), seed), b = Hash01(Mod(x0 + 1, period), Mod(y0, period), seed);
            float c = Hash01(Mod(x0, period), Mod(y0 + 1, period), seed), d = Hash01(Mod(x0 + 1, period), Mod(y0 + 1, period), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static int Mod(int v, int m) => ((v % m) + m) % m;

        /// <summary>GLSL-style smoothstep: 0 below <paramref name="e0"/>, 1 above <paramref name="e1"/> (Mathf.SmoothStep interpolates values instead).</summary>
        private static float Smoothstep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static float WrapDistance(Vector2 a, Vector2 b, int n)
        {
            float dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            dx = Mathf.Min(dx, n - dx);
            dy = Mathf.Min(dy, n - dy);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Writes the PNG, imports it (Repeat, mipmaps, ASTC 6×6 on Android) and returns it with its linear mean luminance.</summary>
        private static (Texture2D, float) Save(string path, Color[] px, int size, bool sRgb)
        {
            ProjectConfigurator.EnsureFolder(TargetFolder);
            double sum = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color l = sRgb ? px[i].linear : px[i];
                sum += 0.3 * l.r + 0.59 * l.g + 0.11 * l.b;
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !sRgb);
            try
            {
                tex.SetPixels(px);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("EnvTextureBaker: no importer for " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = sRgb;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = size,
                format = TextureImporterFormat.ASTC_6x6,
                textureCompression = TextureImporterCompression.Compressed
            });
            importer.SaveAndReimport();
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(path), (float)(sum / px.Length));
        }
    }
}
