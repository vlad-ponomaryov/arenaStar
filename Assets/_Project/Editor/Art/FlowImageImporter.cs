using System;
using System.IO;
using Arena.Editor.Setup;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art
{
    /// <summary>
    /// Brings Google Flow images (Art-Spec §5.2, D-111) from <c>Art/Source/Flow/</c> (outside Assets) into the project as processed
    /// textures: seam feathering of near-tileable images, area downscale to a power of two, a gentle per-channel gain to the
    /// palette mean (Art-Spec §1.3). Only processed files land in <c>Assets/_Project/Art/Textures/Flow/</c>, each with a line in
    /// <c>Docs/AssetLicenses.md</c>. Without the source (e.g. LFS not pulled) the previously imported PNG is kept.
    /// </summary>
    public static class FlowImageImporter
    {
        public const string SourceRoot = "Art/Source/Flow";
        public const string TargetFolder = ViewAssetBuilder.TexturesFolder + "/Flow";

        /// <summary>
        /// Imports a tileable texture: <paramref name="source"/> (repo-relative PNG/JPEG) → <paramref name="assetPath"/> of
        /// <paramref name="size"/>² px, mean colour moved towards <paramref name="targetMean"/> (sRGB) by at most
        /// <paramref name="maxGain"/> per channel. Returns the texture and its mean luminance (0–1, for the shader's macro variation).
        /// </summary>
        public static (Texture2D texture, float meanLuminance) ImportTileable(string source, string assetPath, int size, Color targetMean,
            float maxGain, int seamFeatherPx)
        {
            if (File.Exists(source))
            {
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!src.LoadImage(File.ReadAllBytes(source))) throw new InvalidOperationException("FlowImageImporter: cannot decode " + source);
                    int w = src.width, h = src.height;
                    Color[] px = src.GetPixels();
                    FeatherSeams(px, w, h, seamFeatherPx);
                    Color[] small = Downscale(px, w, h, size);
                    MatchMean(small, targetMean, maxGain);
                    var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    try
                    {
                        outTex.SetPixels(small);
                        outTex.Apply();
                        ProjectConfigurator.EnsureFolder(TargetFolder);
                        File.WriteAllBytes(assetPath, outTex.EncodeToPNG());
                    }
                    finally
                    {
                        Object.DestroyImmediate(outTex);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(src);
                }
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            else if (!File.Exists(assetPath))
            {
                throw new FileNotFoundException("FlowImageImporter: neither the source nor the imported texture exists", source);
            }
            else
            {
                Debug.LogWarning("FlowImageImporter: source " + source + " not found, keeping " + assetPath);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
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
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            return (texture, MeanLuminance(assetPath));
        }

        /// <summary>Removes a small tiling seam: half of the per-row (per-column) jump across the wrap is spread over
        /// <paramref name="feather"/> pixels on each side, the jump smoothed over neighbouring rows to avoid streaks.</summary>
        public static void FeatherSeams(Color[] px, int w, int h, int feather)
        {
            if (feather <= 0) return;
            const int Smooth = 4;
            // Horizontal wrap (left/right edges).
            var jump = new Color[h];
            for (int y = 0; y < h; y++) jump[y] = px[y * w + w - 1] - px[y * w];
            Color[] jx = SmoothWrap(jump, Smooth);
            for (int y = 0; y < h; y++)
                for (int d = 0; d < feather && d < w / 2; d++)
                {
                    float t = 0.5f * (1f - (float)d / feather);
                    px[y * w + d] += jx[y] * t;
                    px[y * w + w - 1 - d] -= jx[y] * t;
                }
            // Vertical wrap (bottom/top edges).
            jump = new Color[w];
            for (int x = 0; x < w; x++) jump[x] = px[(h - 1) * w + x] - px[x];
            Color[] jy = SmoothWrap(jump, Smooth);
            for (int x = 0; x < w; x++)
                for (int d = 0; d < feather && d < h / 2; d++)
                {
                    float t = 0.5f * (1f - (float)d / feather);
                    px[d * w + x] += jy[x] * t;
                    px[(h - 1 - d) * w + x] -= jy[x] * t;
                }
        }

        private static Color[] SmoothWrap(Color[] v, int radius)
        {
            int n = v.Length;
            var o = new Color[n];
            for (int i = 0; i < n; i++)
            {
                Color s = Color.clear;
                for (int k = -radius; k <= radius; k++) s += v[((i + k) % n + n) % n];
                o[i] = s / (2 * radius + 1);
            }
            return o;
        }

        /// <summary>Area average to <paramref name="size"/>² (wrapping, so the result tiles like the source).</summary>
        private static Color[] Downscale(Color[] px, int w, int h, int size)
        {
            var o = new Color[size * size];
            float sx = (float)w / size, sy = (float)h / size;
            for (int y = 0; y < size; y++)
            {
                int y0 = Mathf.FloorToInt(y * sy), y1 = Mathf.Max(y0 + 1, Mathf.FloorToInt((y + 1) * sy));
                for (int x = 0; x < size; x++)
                {
                    int x0 = Mathf.FloorToInt(x * sx), x1 = Mathf.Max(x0 + 1, Mathf.FloorToInt((x + 1) * sx));
                    Color s = Color.clear;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++) s += px[(yy % h) * w + (xx % w)];
                    Color c = s / ((y1 - y0) * (x1 - x0));
                    c.a = 1f;
                    o[y * size + x] = c;
                }
            }
            return o;
        }

        private static void MatchMean(Color[] px, Color target, float maxGain)
        {
            Color mean = Mean(px);
            float gr = Gain(target.r, mean.r, maxGain), gg = Gain(target.g, mean.g, maxGain), gb = Gain(target.b, mean.b, maxGain);
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(Mathf.Clamp01(px[i].r * gr), Mathf.Clamp01(px[i].g * gg), Mathf.Clamp01(px[i].b * gb), 1f);
            Debug.Log("FlowImageImporter: mean " + ColorUtility.ToHtmlStringRGB(mean) + " → " + ColorUtility.ToHtmlStringRGB(Mean(px)) +
                      " (target " + ColorUtility.ToHtmlStringRGB(target) + ")");
        }

        private static float Gain(float target, float mean, float maxGain) =>
            mean <= 1e-4f ? 1f : Mathf.Clamp(target / mean, 1f / maxGain, maxGain);

        private static Color Mean(Color[] px)
        {
            Color s = Color.clear;
            for (int i = 0; i < px.Length; i++) s += px[i];
            return s / px.Length;
        }

        /// <summary>Mean luminance of the imported PNG as stored (sRGB values, as the shader's sample of an sRGB texture is linear —
        /// the ratio macro/mean is what matters, computed on linear values).</summary>
        private static float MeanLuminance(string assetPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                tex.LoadImage(File.ReadAllBytes(assetPath));
                Color[] px = tex.GetPixels();
                double s = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    Color l = px[i].linear;
                    s += 0.3 * l.r + 0.59 * l.g + 0.11 * l.b;
                }
                return (float)(s / px.Length);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
