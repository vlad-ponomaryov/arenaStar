using Arena.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Screen-space overlay canvas for texts that follow world points (damage numbers, own health number): created at match
    /// start under the runner, below the HUD controls (sorting order), no raycasts. Texts use the built-in
    /// <c>LegacyRuntime.ttf</c> font (D-018) and are placed by projecting their world point with the match camera.
    /// </summary>
    public sealed class FloatingTextLayer
    {
        public const string BuiltinFontName = "LegacyRuntime.ttf";

        // Under the HUD canvases (they use 0 and above).
        private const int SortingOrder = -10;
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        private readonly Font _font;

        public Canvas Canvas { get; }

        public FloatingTextLayer(Transform parent, FeelConfig feel)
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
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
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
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            go.AddComponent<Outline>();
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
