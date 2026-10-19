using Arena.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Screen-space overlay canvas for texts that follow world points (damage numbers, own health number): created at match
    /// start under the runner, below the HUD controls (sorting order), no raycasts. Texts use the font of
    /// <c>ViewCatalog.TextFont</c> (Nunito ExtraBold, A.6; the built-in <c>LegacyRuntime.ttf</c> without it) with a dark outline
    /// and are placed by projecting their world point with the match camera.
    /// </summary>
    public sealed class FloatingTextLayer
    {
        public const string BuiltinFontName = "LegacyRuntime.ttf";

        // Under the HUD canvases (they use 0 and above).
        private const int SortingOrder = -10;
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        private readonly Font _font;
        private readonly bool _bold;
        private readonly Color _outlineColor;
        private readonly Vector2 _outlineDistance;

        public Canvas Canvas { get; }

        /// <param name="font">Null — the built-in font.</param>
        /// <param name="outlineColor">Outline of the texts; with a zero <paramref name="outlineDistance"/> — the default outline.</param>
        public FloatingTextLayer(Transform parent, FeelConfig feel, Font font = null, Color outlineColor = default,
            Vector2 outlineDistance = default)
        {
            var go = new GameObject("FloatingTextCanvas", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = SortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = feel.TextReferenceResolution;
            scaler.matchWidthOrHeight = feel.TextMatchWidthOrHeight;
            _font = font != null ? font : Resources.GetBuiltinResource<Font>(BuiltinFontName);
            _bold = font == null;
            _outlineColor = outlineColor;
            _outlineDistance = outlineDistance;
        }

        public Text CreateText(string name, int fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(Canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Centre;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            // The built-in font needs the synthetic bold; Nunito ExtraBold is bold already.
            t.fontStyle = _bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Outline outline = go.AddComponent<Outline>();
            if (_outlineDistance != Vector2.zero)
            {
                outline.effectColor = _outlineColor;
                outline.effectDistance = _outlineDistance;
            }
            go.SetActive(false);
            return t;
        }

        /// <summary>Puts <paramref name="text"/> over <paramref name="world"/>; hides it if the point is behind the camera.</summary>
        public static bool Place(Text text, Camera camera, Vector3 world)
        {
            if (camera == null) return false;
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;
            text.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
            return true;
        }
    }
}
