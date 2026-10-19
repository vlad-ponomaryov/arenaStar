using System;
using System.IO;
using Arena.UI.Style;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace Arena.Editor.Art
{
    /// <summary>
    /// Bakes the "wood and leaves" UI sprites in code (A.6, Art-Spec §4.2): rounded shapes from signed distances, 4×4 samples per
    /// pixel (anti-aliasing = drawing at 4× and averaging), saved as PNG under <see cref="SpritesFolder"/> and imported as 9-slice
    /// sprites. Colours come from the <see cref="UiStyle"/> palette (§1.3) seeded before the bake; the button edge/face/gloss are
    /// white (tinted by the image colour), the panel, plank and leaf are coloured in the sprite. Called by
    /// <see cref="ViewAssetBuilder"/> (<c>SeedUiStyle</c>); idempotent, GUIDs are kept.
    /// </summary>
    public static class UiSpriteBaker
    {
        public const string SpritesFolder = ViewAssetBuilder.UiFolder + "/Sprites";
        public const string EdgePath = SpritesFolder + "/UiButtonEdge.png";
        public const string FacePath = SpritesFolder + "/UiButtonFace.png";
        public const string GlossPath = SpritesFolder + "/UiButtonGloss.png";
        public const string ShadowPath = SpritesFolder + "/UiShadow.png";
        public const string PanelPath = SpritesFolder + "/UiPanel.png";
        public const string PlankPath = SpritesFolder + "/UiPlank.png";
        public const string LeafPath = SpritesFolder + "/UiLeaf.png";
        public const string BackdropPath = SpritesFolder + "/UiBackdrop.png";
        public const string StickBasePath = SpritesFolder + "/HudStickBase.png";
        public const string KnobPath = SpritesFolder + "/HudKnob.png";
        public const string UltRingPath = SpritesFolder + "/HudUltRing.png";
        public const string GlowPath = SpritesFolder + "/HudGlow.png";

        private const int Samples = 4;
        private const int ShapeSize = 128;
        private const float ShapeRadius = 40f;
        private const int ShapeBorder = 48;
        private const float ShadowBlur = 14f;
        private const float PanelRadius = 44f;
        private const float PanelOutline = 3f;
        private const float PanelWood = 17f;
        private const float PanelHighlight = 2.5f;
        private const int PanelBorder = 60;
        private const int PlankWidth = 256;
        private const int PlankHeight = 96;
        private const float PlankRadius = 22f;
        private const int PlankBorder = 40;
        private const int LeafSize = 128;
        private const int BackdropWidth = 512;
        private const int BackdropHeight = 288;
        private const int BackdropLeaves = 46;
        private const int BackdropSeed = 2061;
        private const float FaceShadeBottom = 0.84f;
        private const float RimShade = 0.42f;

        internal static readonly Color Outline = new Color(0.231f, 0.165f, 0.118f, 1f);
        private static readonly Color LeafLight = new Color(0.50f, 0.85f, 0.36f, 1f);
        private static readonly Color LeafDark = new Color(0.25f, 0.65f, 0.29f, 1f);
        private static readonly Color LeafRib = new Color(0.18f, 0.50f, 0.22f, 1f);
        private static readonly Color PlankWood = new Color(0.79f, 0.56f, 0.36f, 1f);
        private static readonly Color PlankGrain = new Color(0.60f, 0.38f, 0.20f, 1f);
        private static readonly Color PlankBevelLight = new Color(0.93f, 0.74f, 0.50f, 1f);
        private static readonly Color PlankBevelDark = new Color(0.48f, 0.32f, 0.20f, 1f);

        /// <summary>Bakes every sprite and stores them in <paramref name="s"/> (palette fields must be seeded).</summary>
        public static void BakeAll(UiStyle s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (!AssetDatabase.IsValidFolder(SpritesFolder)) AssetDatabase.CreateFolder(ViewAssetBuilder.UiFolder, "Sprites");
            float rimPx = Mathf.Max(1f, Mathf.Round(s.ButtonOutline * s.SliceScale));
            s.ButtonEdgeSprite = Save(BakeEdge(rimPx), EdgePath, ShapeBorder);
            s.ButtonFaceSprite = Save(BakeFace(ShapeRadius - rimPx), FacePath, ShapeBorder);
            s.ButtonGlossSprite = Save(BakeGloss(), GlossPath, ShapeBorder);
            s.ShadowSprite = Save(BakeShadow(), ShadowPath, ShapeBorder + 8);
            s.PanelSprite = Save(BakePanel(s.PanelColor, s.PanelBorderColor), PanelPath, PanelBorder);
            s.PlankSprite = Save(BakePlank(), PlankPath, PlankBorder);
            s.LeafSprite = Save(BakeLeaf(), LeafPath, 0);
            s.BackdropSprite = Save(BakeBackdrop(s.BackdropTopColor, s.BackdropBottomColor, s.BackdropLeafColor), BackdropPath, 0);
            // A.7a match HUD: stick bases, toy knobs, glow (white, tinted by the HUD), then the icons. A.7c: the thin ult charge
            // ring replaces the petals of the ult flower.
            s.StickBaseSprite = Save(BakeStickBase(), StickBasePath, 0);
            s.KnobSprite = Save(BakeKnob(), KnobPath, 0);
            s.UltRingSprite = Save(BakeUltRing(), UltRingPath, 0);
            s.GlowSprite = Save(BakeGlow(), GlowPath, 0);
            IconBaker.BakeAll(s);
        }

        private const int HudSize = 128;
        private const float HudOutline = 4f;
        private const float StickRim = 10f;
        private const float StickInnerAlpha = 0.38f;
        private const float StickOutlineAlpha = 0.55f;
        private const float UltRingWidth = 11f;
        private const float UltRingOutline = 2.2f;

        private static Texture2D BakeStickBase()
        {
            float c = HudSize * 0.5f, r = c - 1f;
            return Render(HudSize, HudSize, (x, y) =>
            {
                float d = Length(x - c, y - c) - r;
                if (d > 0f) return Color.clear;
                if (d > -HudOutline * 0.75f) return new Color(Outline.r, Outline.g, Outline.b, StickOutlineAlpha);
                if (d > -HudOutline * 0.75f - StickRim) return new Color(1f, 1f, 1f, 0.95f);
                // Translucent inside, a little denser next to the rim.
                float t = Smooth(0f, 18f, -d - HudOutline * 0.75f - StickRim);
                return new Color(1f, 1f, 1f, Mathf.Lerp(StickInnerAlpha * 1.5f, StickInnerAlpha, t));
            });
        }

        private static Texture2D BakeKnob()
        {
            float c = HudSize * 0.5f, r = c - 1f;
            return Render(HudSize, HudSize, (x, y) =>
            {
                float d = Length(x - c, y - c) - r;
                if (d > 0f) return Color.clear;
                if (d > -HudOutline) return Outline;
                // Face: lighter at the top, a darker crescent at the bottom (the toy "edge"), gloss ellipse on top.
                float v = Mathf.Lerp(FaceShadeBottom, 1f, Smooth(0f, HudSize, y));
                float lower = Length(x - c, y - c + 9f) - (r - HudOutline - 2f);
                if (lower > 0f) v *= 0.8f;
                float gloss = Length((x - c) / 34f, (y - c - 30f) / 15f);
                Color face = new Color(v, v, v, 1f);
                if (gloss < 1f) face = Color.Lerp(face, Color.white, 0.75f * Smooth(1f, 0.5f, gloss));
                return face;
            });
        }

        /// <summary>
        /// A.7c ult charge ring: a thin white band with a soft dark outline on both edges (readable over the sand and the grass),
        /// filled radially by the HUD (Image.Type.Filled) and tinted by the charge state.
        /// </summary>
        private static Texture2D BakeUltRing()
        {
            float c = HudSize * 0.5f, outer = c - 1f, inner = outer - UltRingWidth;
            return Render(HudSize, HudSize, (x, y) =>
            {
                float r = Length(x - c, y - c);
                if (r > outer || r < inner) return Color.clear;
                float edge = Mathf.Min(outer - r, r - inner);
                if (edge < UltRingOutline) return new Color(Outline.r, Outline.g, Outline.b, 0.7f);
                return Color.white;
            });
        }

        private static Texture2D BakeGlow()
        {
            float c = HudSize * 0.5f;
            return Render(HudSize, HudSize, (x, y) =>
            {
                float r = Length(x - c, y - c) / c;
                if (r >= 1f) return Color.clear;
                float a = (1f - r) * (1f - r);
                return new Color(1f, 1f, 1f, a);
            });
        }

        private static float Length(float x, float y) => Mathf.Sqrt(x * x + y * y);

        /// <summary>Signed distance to a rounded box centred at (cx, cy) with half size (hx, hy) and corner radius r.</summary>
        public static float RoundBox(float x, float y, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x - cx) - hx + r;
            float qy = Mathf.Abs(y - cy) - hy + r;
            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        internal delegate Color PixelFn(float x, float y);

        /// <summary>Renders <paramref name="shader"/> (straight alpha) with 4×4 samples per pixel into a texture.</summary>
        internal static Texture2D Render(int w, int h, PixelFn shader)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            const float inv = 1f / (Samples * Samples);
            for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sy = 0; sy < Samples; sy++)
                for (int sx = 0; sx < Samples; sx++)
                {
                    Color c = shader(px + (sx + 0.5f) / Samples, py + (sy + 0.5f) / Samples);
                    r += c.r * c.a;
                    g += c.g * c.a;
                    b += c.b * c.a;
                    a += c.a;
                }
                pixels[py * w + px] = a > 0f ? new Color(r / a, g / a, b / a, a * inv) : new Color(1f, 1f, 1f, 0f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D BakeEdge(float rimPx)
        {
            float half = ShapeSize * 0.5f;
            return Render(ShapeSize, ShapeSize, (x, y) =>
            {
                float d = RoundBox(x, y, half, half, half - 1f, half - 1f, ShapeRadius);
                if (d > 0f) return Color.clear;
                float v = d > -rimPx ? RimShade : 1f;
                return new Color(v, v, v, 1f);
            });
        }

        private static Texture2D BakeFace(float radius)
        {
            float half = ShapeSize * 0.5f;
            return Render(ShapeSize, ShapeSize, (x, y) =>
            {
                float d = RoundBox(x, y, half, half, half - 1f, half - 1f, radius);
                if (d > 0f) return Color.clear;
                float v = Mathf.Lerp(FaceShadeBottom, 1f, Mathf.SmoothStep(0f, 1f, y / ShapeSize));
                return new Color(v, v, v, 1f);
            });
        }

        private static Texture2D BakeGloss()
        {
            float half = ShapeSize * 0.5f;
            return Render(ShapeSize, ShapeSize, (x, y) =>
            {
                float d = RoundBox(x, y, half, half, half - 1f, half - 1f, ShapeRadius * 0.8f);
                if (d > 0f) return Color.clear;
                return new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, y / ShapeSize));
            });
        }

        private static Texture2D BakeShadow()
        {
            float half = ShapeSize * 0.5f;
            return Render(ShapeSize, ShapeSize, (x, y) =>
            {
                float d = RoundBox(x, y, half, half, half - ShadowBlur, half - ShadowBlur, ShapeRadius - ShadowBlur * 0.5f);
                float a = 1f - Smooth(-ShadowBlur, ShadowBlur, d);
                return new Color(1f, 1f, 1f, a);
            });
        }

        private static Texture2D BakePanel(Color cream, Color wood)
        {
            float half = ShapeSize * 0.5f;
            Color dark = Color.Lerp(wood, Outline, 0.55f);
            Color woodLight = Color.Lerp(wood, Color.white, 0.18f);
            Color highlight = Color.Lerp(wood, cream, 0.55f);
            Color creamShade = Color.Lerp(cream, wood, 0.12f);
            return Render(ShapeSize, ShapeSize, (x, y) =>
            {
                float d = RoundBox(x, y, half, half, half - 1f, half - 1f, PanelRadius);
                if (d > 0f) return Color.clear;
                if (d > -PanelOutline) return dark;
                if (d > -PanelOutline - PanelWood)
                {
                    // Wood band: lighter towards the outside-top, a thin grain stroke along the band.
                    float t = (-d - PanelOutline) / PanelWood;
                    Color c = Color.Lerp(woodLight, wood, t);
                    if (Mathf.Abs(t - 0.55f) < 0.07f) c = Color.Lerp(c, dark, 0.35f);
                    return c;
                }
                if (d > -PanelOutline - PanelWood - PanelHighlight) return highlight;
                // Cream with a soft inner shade next to the frame.
                float inner = -d - PanelOutline - PanelWood - PanelHighlight;
                return Color.Lerp(creamShade, cream, Smooth(0f, 7f, inner));
            });
        }

        private static Texture2D BakePlank()
        {
            float cx = PlankWidth * 0.5f, cy = PlankHeight * 0.5f;
            return Render(PlankWidth, PlankHeight, (x, y) =>
            {
                float d = RoundBox(x, y, cx, cy, cx - 1f, cy - 1f, PlankRadius);
                if (d > 0f) return Color.clear;
                if (d > -3f) return Outline;
                float top = PlankHeight - 4f - y;
                float bottom = y - 4f;
                if (bottom < 7f) return PlankBevelDark;
                if (top < 4f) return PlankBevelLight;
                Color c = Color.Lerp(PlankWood, PlankBevelLight, Mathf.Clamp01((y - cy) / cy) * 0.35f);
                // Horizontal grain: two wavy strokes (they stretch cleanly along the 9-slice centre).
                float g1 = Mathf.Abs(y - (cy + 12f + 2.5f * Mathf.Sin(x * 0.045f)));
                float g2 = Mathf.Abs(y - (cy - 10f + 2f * Mathf.Sin(x * 0.06f + 1.3f)));
                if (g1 < 1.3f || g2 < 1.1f) c = Color.Lerp(c, PlankGrain, 0.7f);
                return c;
            });
        }

        private static Texture2D BakeLeaf()
        {
            const float angle = 40f * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            const float halfLength = 44f;
            const float width = 0.42f;
            const float outline = 4.5f;
            float c = LeafSize * 0.5f;
            return Render(LeafSize, LeafSize, (x, y) =>
            {
                float lx = (x - c) * cos + (y - c) * sin;
                float ly = -(x - c) * sin + (y - c) * cos;
                float u = lx / halfLength;
                // Stem below the base.
                if (u < -0.9f && u > -1.25f && Mathf.Abs(ly) < 3.5f) return Outline;
                float h = LeafHalfWidth(u, width) * halfLength;
                float hOut = LeafHalfWidth(lx / (halfLength + outline), width) * (halfLength + outline) + outline * 0.6f;
                if (Mathf.Abs(ly) > hOut) return Color.clear;
                if (Mathf.Abs(ly) > h) return Outline;
                if (Mathf.Abs(ly) < 1.6f && u > -0.95f && u < 0.85f) return LeafRib;
                // Side veins.
                float vein = Mathf.Abs(Frac((u + Mathf.Abs(ly) / halfLength * 0.9f) * 3.2f) - 0.5f);
                Color baseColor = ly > 0f ? LeafLight : LeafDark;
                if (vein < 0.05f && Mathf.Abs(ly) < h * 0.8f) baseColor = Color.Lerp(baseColor, LeafRib, 0.45f);
                return baseColor;
            });
        }

        private static float LeafHalfWidth(float u, float width)
        {
            if (u <= -1f || u >= 1f) return -1f;
            float t = 1f - u * u;
            return width * Mathf.Pow(t, 0.8f) * (1f - 0.25f * u);
        }

        private static Texture2D BakeBackdrop(Color top, Color bottom, Color leaf)
        {
            var rng = new Random(BackdropSeed);
            var leaves = new Vector4[BackdropLeaves];
            for (int i = 0; i < leaves.Length; i++)
                leaves[i] = new Vector4((float)rng.NextDouble() * BackdropWidth, (float)rng.NextDouble() * BackdropHeight,
                    (float)rng.NextDouble() * Mathf.PI * 2f, 10f + (float)rng.NextDouble() * 18f);
            var tex = new Texture2D(BackdropWidth, BackdropHeight, TextureFormat.RGBA32, false);
            var pixels = new Color[BackdropWidth * BackdropHeight];
            for (int y = 0; y < BackdropHeight; y++)
            for (int x = 0; x < BackdropWidth; x++)
            {
                float t = (float)y / (BackdropHeight - 1);
                Color c = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, t));
                float coverage = 0f;
                for (int i = 0; i < leaves.Length; i++)
                {
                    Vector4 l = leaves[i];
                    float dx = x - l.x, dy = y - l.y;
                    if (dx * dx + dy * dy > l.w * l.w * 1.2f) continue;
                    float lx = dx * Mathf.Cos(l.z) + dy * Mathf.Sin(l.z);
                    float ly = -dx * Mathf.Sin(l.z) + dy * Mathf.Cos(l.z);
                    float h = LeafHalfWidth(lx / l.w, 0.42f) * l.w;
                    coverage = Mathf.Max(coverage, 1f - Smooth(h - 0.8f, h + 0.8f, Mathf.Abs(ly)));
                }
                c = Color.Lerp(c, leaf, coverage * leaf.a);
                // Soft vignette towards the corners.
                float vx = (float)x / BackdropWidth - 0.5f, vy = t - 0.5f;
                float vignette = 1f - 0.22f * Mathf.Clamp01((vx * vx + vy * vy) * 2.2f);
                pixels[y * BackdropWidth + x] = new Color(c.r * vignette, c.g * vignette, c.b * vignette, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        // GLSL smoothstep (threshold), not Mathf.SmoothStep (which interpolates values) — see the A.3b note.
        internal static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        internal static Sprite Save(Texture2D tex, string path, int border)
        {
            try
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("UiSpriteBaker: sprite not imported: " + path);
            return sprite;
        }
    }
}
