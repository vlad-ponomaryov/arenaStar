using Arena.UI.Controls;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Builds the match controls canvas at runtime from <see cref="UiStyle"/> (D-018): the left half of the screen is the
    /// floating movement stick zone; on the right — the attack stick, the ult stick and the trick button.
    /// Called once at match start.
    /// </summary>
    public static class MatchHudBuilder
    {
        private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        public static MatchHudControls Build(Transform parent, UiStyle style)
        {
            var controls = new MatchHudControls();
            controls.Canvas = CreateCanvas(parent, style);
            Transform root = controls.Canvas.transform;

            controls.MoveStick = CreateMoveStick(root, style);
            controls.AttackStick = CreateFixedStick("AttackStick", root, style, style.AttackStick);
            controls.UltStick = CreateFixedStick("UltStick", root, style, style.UltStick);
            controls.TrickButton = CreateTrickButton(root, style);
            return controls;
        }

        private static Canvas CreateCanvas(Transform parent, UiStyle style)
        {
            var go = new GameObject("MatchHudCanvas", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = style.ReferenceResolution;
            scaler.matchWidthOrHeight = style.MatchWidthOrHeight;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static VirtualStick CreateMoveStick(Transform root, UiStyle style)
        {
            // Zone: left half of the screen, pivot bottom-left, so local coordinates are measured from the corner.
            RectTransform zone = CreateRect("MoveStickZone", root);
            zone.anchorMin = BottomLeft;
            zone.anchorMax = new Vector2(0.5f, 1f);
            zone.pivot = BottomLeft;
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;
            AddTransparentRaycastImage(zone);

            StickStyle s = style.MoveStick;
            RectTransform stickBase = CreateCircle("Base", zone, style, s.BaseSize, s.BaseColor);
            stickBase.localPosition = s.Position;
            RectTransform knob = CreateCircle("Knob", stickBase, style, s.KnobSize, s.KnobColor);

            var stick = zone.gameObject.AddComponent<VirtualStick>();
            stick.Configure(stickBase, knob, StickMode.Floating, s.Radius, style.Gesture);
            return stick;
        }

        private static VirtualStick CreateFixedStick(string name, Transform root, UiStyle style, StickStyle s)
        {
            RectTransform zone = CreateRect(name, root);
            AnchorBottomRight(zone, s.Position, new Vector2(s.HitSize, s.HitSize));
            AddTransparentRaycastImage(zone);

            RectTransform stickBase = CreateCircle("Base", zone, style, s.BaseSize, s.BaseColor);
            RectTransform knob = CreateCircle("Knob", stickBase, style, s.KnobSize, s.KnobColor);

            var stick = zone.gameObject.AddComponent<VirtualStick>();
            stick.Configure(stickBase, knob, StickMode.Fixed, s.Radius, style.Gesture);
            return stick;
        }

        private static TapButton CreateTrickButton(Transform root, UiStyle style)
        {
            RectTransform rt = CreateRect("TrickButton", root);
            AnchorBottomRight(rt, style.TrickButtonPosition, new Vector2(style.TrickButtonSize, style.TrickButtonSize));
            Image image = rt.gameObject.AddComponent<Image>();
            image.sprite = style.CircleSprite;
            image.color = style.TrickButtonColor;
            return rt.gameObject.AddComponent<TapButton>();
        }

        private static void AnchorBottomRight(RectTransform rt, Vector2 positionFromCorner, Vector2 size)
        {
            rt.anchorMin = BottomRight;
            rt.anchorMax = BottomRight;
            rt.pivot = Centre;
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(-positionFromCorner.x, positionFromCorner.y);
        }

        private static RectTransform CreateCircle(string name, Transform parent, UiStyle style, float size, Color color)
        {
            RectTransform rt = CreateRect(name, parent);
            rt.anchorMin = Centre;
            rt.anchorMax = Centre;
            rt.pivot = Centre;
            rt.sizeDelta = new Vector2(size, size);
            rt.localPosition = Vector3.zero;
            Image image = rt.gameObject.AddComponent<Image>();
            image.sprite = style.CircleSprite;
            image.color = color;
            image.raycastTarget = false;
            return rt;
        }

        private static void AddTransparentRaycastImage(RectTransform rt)
        {
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
