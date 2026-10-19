using System;
using Arena.UI.Style;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.Art
{
    /// <summary>
    /// Bakes the flat service icons in code (A.7a, Art-Spec §4.2): two tones + the dark outline <c>#3B2A1E</c>, 128 px, drawn
    /// from signed distances with 4×4 samples per pixel (<see cref="UiSpriteBaker.Render"/>). Every icon is a stack of layers
    /// back to front; each layer paints its fill inside and the outline around it, so parts get outlines where they overlap
    /// (the toy look). Icons: acorn (score, trick beads), paw (kill feed), leaf medal (MVP / victory), stars (defeat),
    /// and the generic ult (sparkle) and trick (double chevron) icons of the HUD buttons — per-fighter 3D icons later (D-134).
    /// </summary>
    public static class IconBaker
    {
        public const string IconsFolder = UiSpriteBaker.SpritesFolder;
        public const string AcornPath = IconsFolder + "/IconAcorn.png";
        public const string PawPath = IconsFolder + "/IconPaw.png";
        public const string LeafMedalPath = IconsFolder + "/IconLeafMedal.png";
        public const string StarsPath = IconsFolder + "/IconStars.png";
        public const string UltPath = IconsFolder + "/IconUlt.png";
        public const string TrickPath = IconsFolder + "/IconTrick.png";

        private const int Size = 128;
        private const float OutlineWidth = 5f;

        private static readonly Color AcornBody = Hex(0xE8A93C);
        private static readonly Color AcornBodyLight = Hex(0xF8D27E);
        private static readonly Color AcornBodyDark = Hex(0xC98228);
        private static readonly Color AcornCap = Hex(0x9A6438);
        private static readonly Color AcornCapDark = Hex(0x7A4A28);
        private static readonly Color PawPad = Hex(0xF5B3A3);
        private static readonly Color PawPadLight = Hex(0xFFDCD2);
        private static readonly Color MedalGold = Hex(0xFFC93C);
        private static readonly Color MedalGoldDark = Hex(0xE09A1C);
        private static readonly Color Ribbon = Hex(0x6EC6F0);
        private static readonly Color RibbonDark = Hex(0x3B8FBF);
        private static readonly Color LeafGreen = Hex(0x4CBF56);
        private static readonly Color LeafGreenDark = Hex(0x2F8F3A);
        private static readonly Color Star = Hex(0xFFD84A);
        private static readonly Color StarLight = Hex(0xFFF0A8);
        private static readonly Color Sparkle = Color.white;
        private static readonly Color SparkleWarm = Hex(0xFFE9A8);

        private delegate float Sdf(float x, float y);
        private delegate Color Fill(float x, float y);

        private readonly struct Layer
        {
            public readonly Sdf Shape;
            public readonly Fill Paint;

            public Layer(Sdf shape, Fill paint)
            {
                Shape = shape;
                Paint = paint;
            }
        }

        /// <summary>Bakes every icon into <paramref name="s"/>.</summary>
        public static void BakeAll(UiStyle s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            s.AcornIcon = Bake(AcornPath, AcornLayers());
            s.PawIcon = Bake(PawPath, PawLayers());
            s.LeafMedalIcon = Bake(LeafMedalPath, LeafMedalLayers());
            s.StarsIcon = Bake(StarsPath, StarsLayers());
            s.UltIcon = Bake(UltPath, UltLayers());
            s.TrickIcon = Bake(TrickPath, TrickLayers());
            EditorUtility.SetDirty(s);
        }

        private static Sprite Bake(string path, Layer[] layers) =>
            UiSpriteBaker.Save(UiSpriteBaker.Render(Size, Size, (x, y) => Draw(layers, x, y)), path, 0);

        private static Color Draw(Layer[] layers, float x, float y)
        {
            Color c = Color.clear;
            for (int i = 0; i < layers.Length; i++)
            {
                float d = layers[i].Shape(x, y);
                if (d < 0f) c = layers[i].Paint(x, y);
                else if (d < OutlineWidth) c = UiSpriteBaker.Outline;
            }
            return c;
        }

        // ---- Icons -------------------------------------------------------------------------------------------------

        private static Layer[] AcornLayers() => new[]
        {
            new Layer((x, y) => RoundRect(x, y, 67f, 106f, 5f, 13f, 4f), (x, y) => AcornCapDark),
            new Layer((x, y) => Min(Egg(x, y, 64f, 52f, 34f, 32f, 44f), Circle(x, y, 64f, 9f, 5f)), AcornBodyPaint),
            new Layer((x, y) => Mathf.Max(Ellipse(x, y, 64f, 72f, 46f, 28f), 64f - y), AcornCapPaint),
        };

        private static Color AcornBodyPaint(float x, float y)
        {
            if (Ellipse(x, y, 50f, 56f, 9f, 16f) < 0f) return AcornBodyLight;
            if (Ellipse(x, y, 64f, 52f, 34f, 38f) > -8f && x > 64f) return AcornBodyDark;
            return AcornBody;
        }

        private static Color AcornCapPaint(float x, float y)
        {
            // Cross-hatched cap scales.
            float a = Frac((x + y) / 13f), b = Frac((x - y) / 13f);
            return a < 0.16f || b < 0.16f ? AcornCapDark : AcornCap;
        }

        private static Layer[] PawLayers() => new[]
        {
            new Layer((x, y) => Ellipse(x, y, 64f, 44f, 34f, 28f), PawPaint(64f, 44f, 34f, 28f)),
            new Layer((x, y) => Ellipse(x, y, 27f, 80f, 13f, 16f), PawPaint(27f, 80f, 13f, 16f)),
            new Layer((x, y) => Ellipse(x, y, 50f, 101f, 13f, 16f), PawPaint(50f, 101f, 13f, 16f)),
            new Layer((x, y) => Ellipse(x, y, 78f, 101f, 13f, 16f), PawPaint(78f, 101f, 13f, 16f)),
            new Layer((x, y) => Ellipse(x, y, 101f, 80f, 13f, 16f), PawPaint(101f, 80f, 13f, 16f)),
        };

        /// <summary>Pad colour with a light spot in its upper left.</summary>
        private static Fill PawPaint(float cx, float cy, float rx, float ry) => (x, y) =>
            Ellipse(x, y, cx - rx * 0.35f, cy + ry * 0.35f, rx * 0.3f, ry * 0.25f) < 0f ? PawPadLight : PawPad;

        private static Layer[] LeafMedalLayers() => new[]
        {
            new Layer((x, y) => Capsule(x, y, 54f, 62f, 40f, 14f, 10f), (x, y) => Ribbon),
            new Layer((x, y) => Capsule(x, y, 74f, 62f, 88f, 14f, 10f), (x, y) => RibbonDark),
            new Layer((x, y) => Circle(x, y, 64f, 76f, 42f), MedalPaint),
            new Layer((x, y) => Leaf(x, y, 64f, 76f, 24f, 0.45f, 40f), LeafPaint),
        };

        private static Color MedalPaint(float x, float y)
        {
            float d = Circle(x, y, 64f, 76f, 31f);
            if (Mathf.Abs(d) < 2f) return MedalGoldDark;
            return d > 0f && y < 76f ? MedalGoldDark : MedalGold;
        }

        private static Color LeafPaint(float x, float y)
        {
            // Rib along the leaf axis (40°).
            float c = Mathf.Cos(40f * Mathf.Deg2Rad), s = Mathf.Sin(40f * Mathf.Deg2Rad);
            float across = -(x - 64f) * s + (y - 76f) * c;
            return Mathf.Abs(across) < 1.8f ? LeafGreenDark : across > 0f ? LeafGreen : Color.Lerp(LeafGreen, LeafGreenDark, 0.35f);
        }

        private static Layer[] StarsLayers() => new[]
        {
            new Layer((x, y) => RoundStar(x - 30f, y - 44f, 24f, 5, 2.9f, 3f), StarPaint(30f, 44f)),
            new Layer((x, y) => RoundStar(x - 98f, y - 44f, 24f, 5, 2.9f, 3f), StarPaint(98f, 44f)),
            new Layer((x, y) => RoundStar(x - 64f, y - 80f, 34f, 5, 2.9f, 4f), StarPaint(64f, 80f)),
        };

        private static Fill StarPaint(float cx, float cy) => (x, y) => x < cx && y > cy ? StarLight : Star;

        private static Layer[] UltLayers() => new[]
        {
            new Layer((x, y) => RoundStar(x - 58f, y - 58f, 50f, 4, 2.6f, 4f), (x, y) => y > x ? Sparkle : SparkleWarm),
            new Layer((x, y) => RoundStar(x - 102f, y - 102f, 18f, 4, 2.6f, 2f), (x, y) => Sparkle),
        };

        private static Layer[] TrickLayers() => new[]
        {
            new Layer((x, y) => Chevron(x, y, 28f), (x, y) => y > 64f ? Sparkle : SparkleWarm),
            new Layer((x, y) => Chevron(x, y, 64f), (x, y) => y > 64f ? Sparkle : SparkleWarm),
        };

        /// <summary>"&gt;" stroke starting at <paramref name="left"/>, 11 px thick.</summary>
        private static float Chevron(float x, float y, float left) =>
            Min(Capsule(x, y, left, 100f, left + 32f, 64f, 11f), Capsule(x, y, left + 32f, 64f, left, 28f, 11f));

        // ---- Signed distances (pixels; approximate for ellipses — fine for a 5 px outline) -----------------------------

        private static float Circle(float x, float y, float cx, float cy, float r) => Length(x - cx, y - cy) - r;

        private static float Ellipse(float x, float y, float cx, float cy, float rx, float ry) =>
            (Length((x - cx) / rx, (y - cy) / ry) - 1f) * Mathf.Min(rx, ry);

        /// <summary>Ellipse with a different vertical radius above and below the centre.</summary>
        private static float Egg(float x, float y, float cx, float cy, float rx, float ryTop, float ryBottom) =>
            Ellipse(x, y, cx, cy, rx, y >= cy ? ryTop : ryBottom);

        private static float RoundRect(float x, float y, float cx, float cy, float hx, float hy, float r) =>
            UiSpriteBaker.RoundBox(x, y, cx, cy, hx, hy, r);

        private static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            float h = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
            return Length(px - dx * h, py - dy * h) - r;
        }

        /// <summary>Leaf: lens along an axis at <paramref name="angleDeg"/>, half length <paramref name="halfLength"/>.</summary>
        private static float Leaf(float x, float y, float cx, float cy, float halfLength, float width, float angleDeg)
        {
            float c = Mathf.Cos(angleDeg * Mathf.Deg2Rad), s = Mathf.Sin(angleDeg * Mathf.Deg2Rad);
            float u = ((x - cx) * c + (y - cy) * s) / halfLength;
            float v = -(x - cx) * s + (y - cy) * c;
            if (u <= -1f || u >= 1f) return Mathf.Max(Mathf.Abs(u) - 1f, 0f) * halfLength + Mathf.Abs(v);
            float half = width * halfLength * Mathf.Pow(1f - u * u, 0.8f);
            return Mathf.Abs(v) - half;
        }

        /// <summary>Star with <paramref name="n"/> points (i. quilez), point up, corners rounded by <paramref name="round"/>.</summary>
        private static float RoundStar(float px, float py, float r, int n, float m, float round)
        {
            r -= round;
            float an = Mathf.PI / n, en = Mathf.PI / m;
            float acx = Mathf.Cos(an), acy = Mathf.Sin(an), ecx = Mathf.Cos(en), ecy = Mathf.Sin(en);
            float bn = Mod(Mathf.Atan2(px, py), 2f * an) - an;
            float len = Length(px, py);
            float qx = len * Mathf.Cos(bn) - r * acx, qy = len * Mathf.Abs(Mathf.Sin(bn)) - r * acy;
            float t = Mathf.Clamp(-(qx * ecx + qy * ecy), 0f, r * acy / ecy);
            qx += ecx * t;
            qy += ecy * t;
            return Length(qx, qy) * Mathf.Sign(qx) - round;
        }

        private static float Min(float a, float b) => a < b ? a : b;
        private static float Length(float x, float y) => Mathf.Sqrt(x * x + y * y);
        private static float Frac(float v) => v - Mathf.Floor(v);
        private static float Mod(float a, float b) => a - b * Mathf.Floor(a / b);

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
