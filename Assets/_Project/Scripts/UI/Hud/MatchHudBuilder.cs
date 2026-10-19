using Arena.UI.Controls;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Builds the match controls canvas at runtime from <see cref="UiStyle"/> (D-018): the left half of the screen is the
    /// floating movement stick zone; on the right — the attack stick, the ult stick (anchored: the aim is measured from the touch
    /// point, D-129) and the trick button. A.7a "toy" skin (Art-Spec §4.1): translucent stick bases with a light rim, glossy
    /// knobs; A.7c: the attack stick is light and translucent like the movement stick; the ult is a small round button (the
    /// knob, trick-button sized, with the ult icon) inside a thin charge ring that fills clockwise, gold with a soft glow when ready, the trick button with its icon and acorn beads for the uses left (<see cref="CreateTrickDots"/>).
    /// Called once at match start.
    /// </summary>
    public static class MatchHudBuilder
    {
        private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        /// <param name="mirrored">Mirrored layout (setting): the movement zone on the right half, the attack/ult/trick
        /// buttons in the bottom-left corner.</param>
        public static MatchHudControls Build(Transform parent, UiStyle style, bool mirrored = false)
        {
            var controls = new MatchHudControls();
            controls.Canvas = CreateCanvas(parent, style);
            Transform root = controls.Canvas.transform;

            controls.MoveStick = CreateMoveStick(root, style, mirrored);
            controls.AttackStick = CreateFixedStick("AttackStick", root, style, style.AttackStick, mirrored, style.StickBaseSprite);
            Transform attackBase = controls.AttackStick.transform.Find("Base");
            controls.AttackBase = attackBase != null ? attackBase.GetComponent<Image>() : null;
            Transform attackKnob = attackBase != null ? attackBase.Find("Knob") : null;
            controls.AttackKnob = attackKnob != null ? attackKnob.GetComponent<Image>() : null;
            controls.UltStick = CreateFixedStick("UltStick", root, style, style.UltStick, mirrored, style.UltRingSprite);
            Transform ultBase = controls.UltStick.transform.Find("Base");
            controls.UltBase = ultBase != null ? ultBase.GetComponent<Image>() : null;
            Transform ultKnob = ultBase != null ? ultBase.Find("Knob") : null;
            controls.UltKnob = ultKnob != null ? ultKnob.GetComponent<Image>() : null;
            if (ultBase != null) controls.UltRingFill = CreateRingFill(ultBase, style);
            controls.UltGlow = CreateGlow(controls.UltStick.transform, style);
            if (ultKnob != null) controls.UltIcon = CreateIcon("UltIcon", ultKnob, style.UltIcon, style.UltIconSize, style.IconNotReadyColor);
            controls.TrickButton = CreateTrickButton(root, style, mirrored);
            controls.TrickImage = controls.TrickButton.GetComponent<Image>();
            controls.TrickIcon = CreateIcon("TrickIcon", controls.TrickButton.transform, style.TrickIcon, style.TrickIconSize,
                style.IconReadyColor);
            controls.TrickDots = new Image[0];
            return controls;
        }

        /// <summary>Creates <paramref name="count"/> acorn beads in a centred row above the trick button (replaces existing ones).</summary>
        public static void CreateTrickDots(MatchHudControls controls, UiStyle style, int count)
        {
            if (controls.TrickDots != null)
                for (int i = 0; i < controls.TrickDots.Length; i++)
                    if (controls.TrickDots[i] != null) Object.Destroy(controls.TrickDots[i].gameObject);

            controls.TrickDots = new Image[count < 0 ? 0 : count];
            Transform parent = controls.TrickButton.transform;
            float start = -(count - 1) * 0.5f * style.TrickDotSpacing;
            Sprite bead = SpriteOr(style.AcornIcon, style.CircleSprite);
            for (int i = 0; i < controls.TrickDots.Length; i++)
            {
                RectTransform dot = CreateImage("TrickDot" + i, parent, bead, new Vector2(style.TrickDotSize, style.TrickDotSize),
                    style.TrickDotOnColor);
                dot.localPosition = new Vector3(start + i * style.TrickDotSpacing, style.TrickDotOffset, 0f);
                controls.TrickDots[i] = dot.GetComponent<Image>();
            }
        }

        /// <summary>Radial fill over the ring track, under the ult button: clockwise from the top, empty until charged.</summary>
        private static Image CreateRingFill(Transform ultBase, UiStyle style)
        {
            RectTransform fill = CreateImage("RingFill", ultBase, SpriteOr(style.UltRingSprite, style.CircleSprite),
                new Vector2(style.UltStick.BaseSize, style.UltStick.BaseSize), style.UltRingFillColor);
            fill.SetAsFirstSibling(); // under the knob
            Image image = fill.GetComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0f;
            return image;
        }

        private static Image CreateGlow(Transform stickZone, UiStyle style)
        {
            RectTransform glow = CreateImage("Glow", stickZone, SpriteOr(style.GlowSprite, style.CircleSprite),
                new Vector2(style.UltGlowSize, style.UltGlowSize), style.UltGlowColor);
            glow.SetAsFirstSibling(); // behind the ring and the button
            Image image = glow.GetComponent<Image>();
            image.enabled = false;
            return image;
        }

        private static Image CreateIcon(string name, Transform parent, Sprite sprite, float size, Color color)
        {
            if (sprite == null) return null;
            return CreateImage(name, parent, sprite, new Vector2(size, size), color).GetComponent<Image>();
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

        private static VirtualStick CreateMoveStick(Transform root, UiStyle style, bool mirrored)
        {
            // Zone: left (mirrored: right) half of the screen, pivot in its bottom outer corner, so local coordinates are
            // measured from the corner.
            RectTransform zone = CreateRect("MoveStickZone", root);
            zone.anchorMin = mirrored ? new Vector2(0.5f, 0f) : BottomLeft;
            zone.anchorMax = mirrored ? Vector2.one : new Vector2(0.5f, 1f);
            zone.pivot = mirrored ? BottomRight : BottomLeft;
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;
            AddTransparentRaycastImage(zone);

            StickStyle s = style.MoveStick;
            RectTransform stickBase = CreateStickBase(zone, style, s, style.StickBaseSprite);
            stickBase.localPosition = mirrored ? new Vector3(-s.Position.x, s.Position.y, 0f) : (Vector3)s.Position;
            RectTransform knob = CreateKnob(stickBase, style, s);

            var stick = zone.gameObject.AddComponent<VirtualStick>();
            stick.Configure(stickBase, knob, StickMode.Floating, s.Radius, style.Gesture);
            return stick;
        }

        private static VirtualStick CreateFixedStick(string name, Transform root, UiStyle style, StickStyle s, bool mirrored,
            Sprite baseSprite)
        {
            RectTransform zone = CreateRect(name, root);
            AnchorCorner(zone, s.Position, new Vector2(s.HitSize, s.HitSize), mirrored);
            AddTransparentRaycastImage(zone);

            RectTransform stickBase = CreateStickBase(zone, style, s, baseSprite);
            RectTransform knob = CreateKnob(stickBase, style, s);

            var stick = zone.gameObject.AddComponent<VirtualStick>();
            stick.Configure(stickBase, knob, StickMode.Anchored, s.Radius, style.Gesture);
            return stick;
        }

        private static RectTransform CreateStickBase(Transform zone, UiStyle style, StickStyle s, Sprite sprite) =>
            CreateImage("Base", zone, SpriteOr(sprite, style.CircleSprite), new Vector2(s.BaseSize, s.BaseSize), s.BaseColor);

        private static RectTransform CreateKnob(Transform stickBase, UiStyle style, StickStyle s) =>
            CreateImage("Knob", stickBase, SpriteOr(style.KnobSprite, style.CircleSprite), new Vector2(s.KnobSize, s.KnobSize), s.KnobColor);

        private static TapButton CreateTrickButton(Transform root, UiStyle style, bool mirrored)
        {
            RectTransform rt = CreateRect("TrickButton", root);
            AnchorCorner(rt, style.TrickButtonPosition, new Vector2(style.TrickButtonSize, style.TrickButtonSize), mirrored);
            Image image = rt.gameObject.AddComponent<Image>();
            image.sprite = SpriteOr(style.KnobSprite, style.CircleSprite);
            image.color = style.TrickButtonColor;
            return rt.gameObject.AddComponent<TapButton>();
        }

        /// <summary>Bottom-right corner (mirrored: bottom-left), <paramref name="positionFromCorner"/> measured inwards.</summary>
        private static void AnchorCorner(RectTransform rt, Vector2 positionFromCorner, Vector2 size, bool mirrored)
        {
            Vector2 corner = mirrored ? BottomLeft : BottomRight;
            rt.anchorMin = corner;
            rt.anchorMax = corner;
            rt.pivot = Centre;
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(mirrored ? positionFromCorner.x : -positionFromCorner.x, positionFromCorner.y);
        }

        private static RectTransform CreateImage(string name, Transform parent, Sprite sprite, Vector2 size, Color color)
        {
            RectTransform rt = CreateRect(name, parent);
            rt.anchorMin = Centre;
            rt.anchorMax = Centre;
            rt.pivot = Centre;
            rt.sizeDelta = size;
            rt.localPosition = Vector3.zero;
            Image image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return rt;
        }

        private static Sprite SpriteOr(Sprite sprite, Sprite fallback) => sprite != null ? sprite : fallback;

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
