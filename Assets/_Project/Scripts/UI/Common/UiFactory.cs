using System;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Arena.UI.Common
{
    /// <summary>
    /// Small builders for the runtime-built screens (D-018): canvas, text, button, panel, layout rows (uGUI layout groups,
    /// D-040). Sizes, colours, fonts and sprites — from <see cref="UiStyle"/>. A.6 ("wood and leaves", Art-Spec §4.2): Nunito
    /// (Bold for text, ExtraBold from <see cref="UiStyle.LargeFontSize"/>), light text with a thin dark outline, "toy" buttons
    /// (<see cref="ToyButton"/>: shadow, dark edge, tinted face with gloss), cream panels with a wooden frame and leaves, plank
    /// titles. Without the sprites/fonts of the style (bare test styles) everything falls back to plain rectangles and the
    /// built-in <c>LegacyRuntime.ttf</c>.
    /// </summary>
    public static class UiFactory
    {
        public const string BuiltinFontName = "LegacyRuntime.ttf";

        private static Font _font;

        /// <summary>The built-in fallback font.</summary>
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

        /// <summary>Font of a text size: the heading font from <see cref="UiStyle.LargeFontSize"/>, else the body font.</summary>
        public static Font FontFor(UiStyle style, int fontSize)
        {
            Font body = style != null && style.Font != null ? style.Font : Font;
            if (style == null || style.HeadingFont == null || fontSize < style.LargeFontSize) return body;
            return style.HeadingFont;
        }

        /// <summary>Light text (<see cref="UiStyle.TextColor"/>) with the thin dark outline (HUD, coloured buttons, backdrops).</summary>
        public static Text CreateText(string name, Transform parent, UiStyle style, int fontSize, TextAnchor anchor, string text = "")
        {
            RectTransform rt = CreateRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = FontFor(style, fontSize);
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = style.TextColor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            Outline outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = style.TextOutlineColor;
            outline.effectDistance = style.TextOutlineDistance;
            outline.enabled = style.TextOutlineDistance != Vector2.zero;
            return t;
        }

        /// <summary>Makes <paramref name="t"/> dark text without the outline (on cream panels and light buttons).</summary>
        public static Text MakeDark(Text t, UiStyle style)
        {
            t.color = style.TextDarkColor;
            Outline outline = t.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
            return t;
        }

        /// <summary>Makes <paramref name="t"/> light text in <paramref name="color"/> with the outline.</summary>
        public static Text MakeLight(Text t, UiStyle style, Color color)
        {
            t.color = color;
            Outline outline = t.GetComponent<Outline>();
            if (outline != null) outline.enabled = style.TextOutlineDistance != Vector2.zero;
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

        /// <summary>Image of a 9-slice sprite (a plain rectangle without a sprite).</summary>
        public static Image CreateSliced(string name, Transform parent, Sprite sprite, Color color, float sliceScale, bool raycast = false)
        {
            Image image = CreateImage(name, parent, color, sprite, raycast);
            if (sprite != null)
            {
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = sliceScale > 0f ? sliceScale : 1f;
            }
            return image;
        }

        /// <summary>
        /// "Toy" button with a centred label (Art-Spec §4.2): soft shadow, dark bottom edge (<see cref="UiStyle.EdgeFor"/>), face in
        /// <paramref name="color"/> with a gloss; the face sinks onto the edge while pressed (<see cref="ToyButton"/>). The label is
        /// dark on light faces, light with the outline on the others. <see cref="Button.targetGraphic"/> is the face. Its size is
        /// the preferred size in a layout group.
        /// </summary>
        public static Button CreateButton(string name, Transform parent, UiStyle style, Vector2 size, Color color, int fontSize,
            string label, UnityAction onClick, out Text labelText)
        {
            RectTransform rt = CreateRect(name, parent);
            rt.sizeDelta = size;
            SetPreferredSize(rt.gameObject, size);
            float scale = style.SliceScale;
            bool skinned = style.ButtonFaceSprite != null;
            float edge = skinned ? style.ButtonEdgeHeight : 0f;
            float rim = skinned ? style.ButtonOutline : 0f;

            if (style.ShadowSprite != null)
            {
                Image shadow = CreateSliced("Shadow", rt, style.ShadowSprite, style.ShadowColor, scale);
                Stretch(shadow.rectTransform);
                shadow.rectTransform.anchoredPosition = style.ShadowOffset;
            }
            Image edgeImage = CreateSliced("Edge", rt, style.ButtonEdgeSprite, style.EdgeFor(color), scale, true);
            Stretch(edgeImage.rectTransform);
            Image face = CreateSliced("Face", rt, style.ButtonFaceSprite, color, scale, true);
            RectTransform frt = face.rectTransform;
            Stretch(frt);
            frt.offsetMin = new Vector2(rim, edge + rim);
            frt.offsetMax = new Vector2(-rim, -rim);
            Image gloss = null;
            if (style.ButtonGlossSprite != null)
            {
                gloss = CreateSliced("Gloss", frt, style.ButtonGlossSprite, new Color(1f, 1f, 1f, style.ButtonGlossAlpha), scale);
                RectTransform grt = gloss.rectTransform;
                grt.anchorMin = new Vector2(0f, 0.5f);
                grt.anchorMax = Vector2.one;
                grt.offsetMin = new Vector2(rim * 1.5f, 0f);
                grt.offsetMax = new Vector2(-rim * 1.5f, -rim);
            }

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            ColorBlock colors = button.colors;
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.disabledColor = new Color(0.72f, 0.72f, 0.72f, 0.8f);
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(onClick);
            labelText = CreateText("Label", frt, style, fontSize, TextAnchor.MiddleCenter, label);
            Stretch(labelText.rectTransform);
            labelText.rectTransform.anchoredPosition = new Vector2(0f, -fontSize * style.LabelBaselineShift);
            ToyButton toy = rt.gameObject.AddComponent<ToyButton>();
            toy.Init(edgeImage, face, labelText, edge * style.ButtonPressDepth, gloss, style.DisabledLabelAlpha);
            ApplyLabelColor(labelText, style, color);
            return button;
        }

        /// <summary>Recolours a button of <see cref="CreateButton"/>: face, edge and the label contrast (chosen option and so on).</summary>
        public static void SetButtonColor(Button button, UiStyle style, Color color)
        {
            if (button == null) return;
            ToyButton toy = button.GetComponent<ToyButton>();
            if (toy == null)
            {
                if (button.targetGraphic != null) button.targetGraphic.color = color;
                return;
            }
            toy.Face.color = color;
            toy.Edge.color = style.EdgeFor(color);
            ApplyLabelColor(toy.Label, style, color);
        }

        private static void ApplyLabelColor(Text label, UiStyle style, Color face)
        {
            if (label == null) return;
            if (style.IsLightFace(face)) MakeDark(label, style);
            else MakeLight(label, style, style.TextColor);
        }

        /// <summary>
        /// Cream panel with a wooden frame and a soft shadow; with <paramref name="leaves"/> — leaves on the top corners. The panel is
        /// a vertical layout (padding <see cref="UiStyle.PanelPadding"/>) that fits its content; the decorations ignore the layout.
        /// </summary>
        public static RectTransform CreatePanel(string name, Transform parent, UiStyle style, float spacing, bool leaves)
        {
            RectTransform rt = CreateRect(name, parent);
            if (style.ShadowSprite != null)
            {
                Image shadow = CreateSliced("Shadow", rt, style.ShadowSprite, style.ShadowColor, style.SliceScale);
                Stretch(shadow.rectTransform);
                shadow.rectTransform.anchoredPosition = style.ShadowOffset * 1.5f;
                IgnoreLayout(shadow.gameObject);
            }
            Image body = CreateSliced("Body", rt, style.PanelSprite, style.PanelSprite != null ? Color.white : style.PanelColor,
                style.PanelSliceScale, true);
            Stretch(body.rectTransform);
            IgnoreLayout(body.gameObject);
            if (leaves && style.LeafSprite != null)
            {
                AddLeaf(rt, style, new Vector2(0f, 1f), false);
                AddLeaf(rt, style, new Vector2(1f, 1f), true);
            }
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureLayout(layout, spacing);
            int px = Mathf.RoundToInt(style.PanelPadding.x);
            int py = Mathf.RoundToInt(style.PanelPadding.y);
            layout.padding = new RectOffset(px, px, py, py);
            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        private static void AddLeaf(RectTransform panel, UiStyle style, Vector2 corner, bool mirror)
        {
            Image leaf = CreateImage(mirror ? "LeafRight" : "LeafLeft", panel, Color.white, style.LeafSprite);
            RectTransform lrt = leaf.rectTransform;
            lrt.anchorMin = lrt.anchorMax = corner;
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = style.LeafSize;
            float inset = style.LeafSize.x * 0.12f;
            lrt.anchoredPosition = new Vector2(mirror ? -inset : inset, -inset);
            lrt.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            IgnoreLayout(leaf.gameObject);
        }

        /// <summary>Wooden plank with a light heading-font title (title of a panel).</summary>
        public static Text CreatePlankTitle(string name, Transform parent, UiStyle style, float width, int fontSize, string text)
        {
            Image plank = CreateSliced(name, parent, style.PlankSprite, style.PlankSprite != null ? Color.white : style.PanelBorderColor,
                style.SliceScale);
            SetPreferredSize(plank.gameObject, new Vector2(width, style.PlankHeight));
            Text t = CreateText("Text", plank.rectTransform, style, fontSize, TextAnchor.MiddleCenter, text);
            Stretch(t.rectTransform);
            t.rectTransform.anchoredPosition = new Vector2(0f, -fontSize * style.LabelBaselineShift);
            return t;
        }

        /// <summary>Dark text with a fixed preferred size inside a layout group (on a cream panel).</summary>
        public static Text CreatePanelText(string name, Transform parent, UiStyle style, int fontSize, Vector2 size, string text,
            TextAnchor anchor = TextAnchor.MiddleCenter) =>
            MakeDark(CreateLayoutText(name, parent, style, fontSize, size, text, anchor), style);

        /// <summary>Full-screen backdrop that blocks clicks: the baked menu background when <paramref name="useSprite"/>, else <paramref name="dim"/>.</summary>
        public static Image CreateBackdrop(Transform parent, UiStyle style, Color dim, bool useSprite)
        {
            bool sprite = useSprite && style.BackdropSprite != null;
            Image backdrop = CreateImage("Backdrop", parent, sprite ? Color.white : dim, sprite ? style.BackdropSprite : null, true);
            Stretch(backdrop.rectTransform);
            return backdrop;
        }

        /// <summary>Centres <paramref name="rt"/> in its parent (anchors and pivot at the middle).</summary>
        public static void Centre(RectTransform rt, Vector2 offset)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
        }

        /// <summary>Gives a row a fixed width with its children from the left (label columns line up across rows).</summary>
        public static void AlignRowLeft(RectTransform row, float width)
        {
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout != null) layout.childAlignment = TextAnchor.MiddleLeft;
            LayoutElement le = row.GetComponent<LayoutElement>();
            if (le == null) le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
        }

        public static void IgnoreLayout(GameObject go)
        {
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
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
