using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Arena.Core.Map;
using Arena.Data;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.View;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art.Capture
{
    /// <summary>
    /// Environment showcase for the comparison with the arena-target concept (A.3d, brief A-EnvTarget §4): a 9 × 7 piece of map in
    /// the gameplay camera (a 2 × 2 stone block, a 3 × 2 bush mass, three single walls drawn as props, a crate, a 2 × 2 pond) built
    /// through the real <see cref="MapView"/>. Writes <c>env_close.png</c> (the piece cropped from a 2× supersampled gameplay frame)
    /// and <c>env_stats.txt</c> — mean sRGB colour, luminance, hue and saturation of known spots (open sand, sand in the block's
    /// shadow, bush tips and body, block top and side, meadow, water) for <c>Tools/art-compare.py</c>.
    /// </summary>
    public static class EnvShowcase
    {
        /// <summary>Rows from the far side (top) to the near side. W wall, X crate, B bush, ~ water.</summary>
        private static readonly string[] Layout =
        {
            ".........",
            ".WW..BBB.",
            ".WW..BBB.",
            ".........",
            "W...X....",
            "~~.....W.",
            "~~..W...."
        };

        private const int ShotWidth = 1920;
        private const int ShotHeight = 1080;
        private const int Supersample = 2;
        private const float CropMarginCells = 0.6f;
        private const float ShadowEdgeZ = 5.0f;      // across the 2 × 2 block's shadow (median of three rows)
        private const float ShadowEdgeRowGap = 0.5f;
        private const float ShadowEdgeFrom = 3.02f;
        private const float ShadowEdgeTo = 4.4f;
        private const float ShadowEdgeStep = 0.02f;

        /// <summary>A measured spot: world point, world radius of the sample square, how the pixels are reduced.</summary>
        private readonly struct Spot
        {
            public readonly string Name;
            public readonly Vector3 Point;
            public readonly float Radius;
            public readonly SpotReduce Reduce;

            public Spot(string name, Vector3 point, float radius, SpotReduce reduce)
            {
                Name = name;
                Point = point;
                Radius = radius;
                Reduce = reduce;
            }
        }

        private enum SpotReduce { Mean, BrightestFifteenPercent, DarkerHalf }

        public static void Save(Camera camera, CameraConfig config, ViewCatalog catalog, Light light, string dir)
        {
            int height = Layout.Length, width = Layout[0].Length;
            var tiles = new TileType[width * height];
            for (int r = 0; r < height; r++)
            {
                string line = Layout[height - 1 - r];
                for (int c = 0; c < width; c++)
                    tiles[r * width + c] = line[c] == 'W' ? TileType.Wall : line[c] == 'X' ? TileType.BreakableWall
                        : line[c] == 'B' ? TileType.Bush : line[c] == '~' ? TileType.Water : TileType.Floor;
            }
            var root = new GameObject("EnvShowcase");
            try
            {
                var grid = new TileGrid(width, height, tiles);
                MapView.Create(root.transform, catalog, in grid);
                Quaternion rotation = CameraMath.Rotation(config.PitchDegrees, false);
                var focus = new Vector3(width * 0.5f, 0f, height * 0.5f);
                camera.orthographic = false;
                camera.fieldOfView = config.FieldOfView;
                camera.transform.SetPositionAndRotation(CameraMath.Position(focus, rotation, config.Distance), rotation);
                MatchLightRig.Apply(light, 0f, catalog);
                Texture2D big = ArtCapture.Render(camera, ShotWidth * Supersample, ShotHeight * Supersample);
                Texture2D shot = ArtCapture.Downscale(big, Supersample);
                Object.DestroyImmediate(big);
                camera.aspect = ShotWidth / (float)ShotHeight;
                try
                {
                    WriteStats(camera, catalog, shot, Path.Combine(dir, "env_stats.txt"));
                    SaveCrop(camera, shot, width, height, catalog.WallHeight, dir);
                }
                finally
                {
                    camera.ResetAspect();
                    Object.DestroyImmediate(shot);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                MapShadowMask.Clear();
            }
        }

        private static void SaveCrop(Camera camera, Texture2D shot, int width, int height, float wallHeight, string dir)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? -CropMarginCells : width + CropMarginCells, (i & 4) == 0 ? 0f : wallHeight,
                    (i & 2) == 0 ? -CropMarginCells : height + CropMarginCells);
                Vector3 v = camera.WorldToViewportPoint(p);
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX * shot.width), 0, shot.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY * shot.height), 0, shot.height - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX * shot.width), x0 + 1, shot.width);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY * shot.height), y0 + 1, shot.height);
            var crop = new Texture2D(x1 - x0, y1 - y0, TextureFormat.RGBA32, false);
            crop.SetPixels32(ArtCapture.Crop(shot, x0, y0, x1 - x0, y1 - y0));
            crop.Apply();
            ArtCapture.Save(crop, dir, "env_close");
            Object.DestroyImmediate(crop);
        }

        private static void WriteStats(Camera camera, ViewCatalog catalog, Texture2D shot, string path)
        {
            float h = catalog.WallHeight;
            var spots = new List<Spot>
            {
                new Spot("sand_open", new Vector3(6.5f, 0f, 2.5f), 0.25f, SpotReduce.Mean),
                new Spot("sand_shadow", new Vector3(3.14f, 0f, 4.25f), 0.12f, SpotReduce.Mean),   // A.3f: blocks 0.9 wide and 0.9 tall
                new Spot("bush_tips", new Vector3(6.5f, 0.9f, 4.5f), 0.45f, SpotReduce.BrightestFifteenPercent),
                new Spot("bush_body", new Vector3(6.5f, 0.9f, 4.5f), 0.45f, SpotReduce.DarkerHalf),
                new Spot("block_top", new Vector3(1.5f, h - 0.04f, 4.5f), 0.3f, SpotReduce.BrightestFifteenPercent),   // A.3f: the cream plate of a block, not the leaf or the gap
                new Spot("block_side", new Vector3(1.5f, h * 0.55f, 4.04f), 0.12f, SpotReduce.Mean),
                new Spot("meadow", new Vector3(-3f, 0f, 3f), 0.5f, SpotReduce.Mean),
                new Spot("water", new Vector3(1f, catalog.WaterHeight, 1f), 0.25f, SpotReduce.Mean)
            };
            Color32[] px = shot.GetPixels32();
            var sb = new StringBuilder();
            sb.AppendLine("# A.3d env_stats: name hex lum hue sat pixels (sRGB, gameplay light of team 0, env_close stand)");
            var samples = new List<Color>();
            foreach (Spot s in spots)
            {
                Vector2 c = ToPixels(camera, shot, s.Point);
                Vector2 e = ToPixels(camera, shot, s.Point + camera.transform.right * s.Radius);
                int r = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(c, e)));
                samples.Clear();
                for (int y = Mathf.RoundToInt(c.y) - r; y <= Mathf.RoundToInt(c.y) + r; y++)
                for (int x = Mathf.RoundToInt(c.x) - r; x <= Mathf.RoundToInt(c.x) + r; x++)
                {
                    if (x < 0 || y < 0 || x >= shot.width || y >= shot.height) continue;
                    samples.Add(px[y * shot.width + x]);
                }
                Color mean = Reduce(samples, s.Reduce);
                Color.RGBToHSV(mean, out float hue, out float sat, out _);
                sb.Append(s.Name).Append(' ').Append('#').Append(ColorUtility.ToHtmlStringRGB(mean)).Append(' ')
                    .Append(Luminance(mean).ToString("0.000", CultureInfo.InvariantCulture)).Append(' ')
                    .Append((hue * 360f).ToString("0", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(sat.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(samples.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }
            AppendShadowEdge(camera, shot, px, sb);
            File.WriteAllText(path, sb.ToString());
            Debug.Log("ArtCapture: wrote " + path + "\n" + sb);
        }

        /// <summary>
        /// A.3e: width of the block shadow's edge — a luminance profile on the sand from inside the shadow to the east (world z
        /// <see cref="ShadowEdgeZ"/>), 10 %–90 % of its range, in cells. Written in the stats format with the width in the lum field.
        /// </summary>
        private static void AppendShadowEdge(Camera camera, Texture2D shot, Color32[] px, StringBuilder sb)
        {
            var lum = new List<float>();
            for (float x = ShadowEdgeFrom; x <= ShadowEdgeTo; x += ShadowEdgeStep)
            {
                // Median of three rows across the shadow: a path stone on one row does not bend the profile.
                float a = SandLuminance(camera, shot, px, x, ShadowEdgeZ - ShadowEdgeRowGap);
                float b = SandLuminance(camera, shot, px, x, ShadowEdgeZ);
                float c = SandLuminance(camera, shot, px, x, ShadowEdgeZ + ShadowEdgeRowGap);
                lum.Add(Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c)));
            }
            float min = float.MaxValue, max = float.MinValue;
            foreach (float l in lum) { min = Mathf.Min(min, l); max = Mathf.Max(max, l); }
            int i10 = -1, i90 = -1;
            for (int i = 0; i < lum.Count; i++)
            {
                float t = (lum[i] - min) / Mathf.Max(1e-4f, max - min);
                if (i10 < 0 && t >= 0.1f) i10 = i;
                if (i90 < 0 && t >= 0.9f) i90 = i;
            }
            float width = i10 >= 0 && i90 >= 0 ? (i90 - i10) * ShadowEdgeStep : 0f;
            sb.Append("shadow_edge #000000 ").Append(width.ToString("0.000", CultureInfo.InvariantCulture)).Append(" 0 0.00 ")
                .Append(lum.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }

        private static float SandLuminance(Camera camera, Texture2D shot, Color32[] px, float x, float z)
        {
            Vector2 p = ToPixels(camera, shot, new Vector3(x, 0f, z));
            float sum = 0f;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ix = Mathf.RoundToInt(p.x) + dx, iy = Mathf.RoundToInt(p.y) + dy;
                if (ix < 0 || iy < 0 || ix >= shot.width || iy >= shot.height) continue;
                sum += Luminance(px[iy * shot.width + ix]);
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        private static Vector2 ToPixels(Camera camera, Texture2D shot, Vector3 world)
        {
            Vector3 v = camera.WorldToViewportPoint(world);
            return new Vector2(v.x * shot.width, v.y * shot.height);
        }

        /// <summary>Relative luminance of an sRGB colour (Rec. 709 weights on the sRGB values — a screen brightness, not linear).</summary>
        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color Reduce(List<Color> samples, SpotReduce reduce)
        {
            if (samples.Count == 0) return Color.black;
            if (reduce != SpotReduce.Mean) samples.Sort((a, b) => Luminance(a).CompareTo(Luminance(b)));
            int from = 0, to = samples.Count;
            if (reduce == SpotReduce.BrightestFifteenPercent) from = Mathf.Min(samples.Count - 1, Mathf.FloorToInt(samples.Count * 0.85f));
            if (reduce == SpotReduce.DarkerHalf) to = Mathf.Max(1, samples.Count / 2);
            float r = 0f, g = 0f, b = 0f;
            for (int i = from; i < to; i++) { r += samples[i].r; g += samples[i].g; b += samples[i].b; }
            int n = to - from;
            return new Color(r / n, g / n, b / n, 1f);
        }
    }
}
