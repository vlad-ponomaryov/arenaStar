using System;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Arena.UI.Common
{
    /// <summary>
    /// Small builders for the runtime-built screens (D-018): canvas, text, button, panel, layout rows (uGUI layout groups,
    /// D-040). Font — the built-in <c>LegacyRuntime.ttf</c>; sizes and colours — from <see cref="UiStyle"/>.
    /// </summary>
    public static class UiFactory
    {
        public const string BuiltinFontName = "LegacyRuntime.ttf";

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                return _font;
            }
        }

        public static Canvas CreateCanvas(string name, Transform parent, UiStyle style, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = style.ReferenceResolution;
            scaler.matchWidthOrHeight = style.MatchWidthOrHeight;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Stretches <paramref name="rt"/> over its parent.</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static Text CreateText(string name, Transform parent, UiStyle style, int fontSize, TextAnchor anchor, string text = "")
        {
            RectTransform rt = CreateRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = style.TextColor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            return t;
        }

        public static Image CreateImage(string name, Transform parent, Color color, Sprite sprite = null, bool raycast = false)
        {
            RectTransform rt = CreateRect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>Button with a centred label; its size is the preferred size in a layout group.</summary>
        public static Button CreateButton(string name, Transform parent, UiStyle style, Vector2 size, Color color, int fontSize,
            string label, UnityAction onClick, out Text labelText)
        {
            Image image = CreateImage(name, parent, color, null, true);
            RectTransform rt = image.rectTransform;
            rt.sizeDelta = size;
            SetPreferredSize(rt.gameObject, size);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);
            labelText = CreateText("Label", rt, style, fontSize, TextAnchor.MiddleCenter, label);
            Stretch(labelText.rectTransform);
            return button;
        }

        public static void SetPreferredSize(GameObject go, Vector2 size)
        {
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            le.minWidth = size.x;
            le.minHeight = size.y;
        }

        /// <summary>Vertical stack centred in its parent (children keep their preferred sizes).</summary>
        public static RectTransform CreateColumn(string name, Transform parent, float spacing)
        {
            RectTransform rt = CreateRect(name, parent);
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureLayout(layout, spacing);
            return rt;
        }

        /// <summary>Horizontal row of children with preferred sizes, centred.</summary>
        public static RectTransform CreateRow(string name, Transform parent, float spacing, float height)
        {
            RectTransform rt = CreateRect(name, parent);
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureLayout(layout, spacing);
            if (height > 0f)
            {
                LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = height;
                le.minHeight = height;
            }
            return rt;
        }

        /// <summary>Text with a fixed preferred size inside a layout group.</summary>
        public static Text CreateLayoutText(string name, Transform parent, UiStyle style, int fontSize, Vector2 size, string text,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            Text t = CreateText(name, parent, style, fontSize, anchor, text);
            SetPreferredSize(t.gameObject, size);
            return t;
        }

        private static void ConfigureLayout(HorizontalOrVerticalLayoutGroup layout, float spacing)
        {
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        /// <summary>Destroys all children of <paramref name="t"/> (rebuilding a screen after a language switch).</summary>
        public static void DestroyChildren(Transform t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
