using Arena.UI.Controls;
using UnityEngine;

namespace Arena.UI.Style
{
    /// <summary>
    /// Sizes, margins, colours and built-in sprites of the runtime-built UI (D-018). Created by
    /// <c>Arena.Editor.Art.ViewAssetBuilder</c>; canvas units are at <see cref="ReferenceResolution"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "UiStyle", menuName = "Arena/UI Style")]
    public sealed class UiStyle : ScriptableObject
    {
        [Header("Canvas")]
        public Vector2 ReferenceResolution;

        [Range(0f, 1f)] public float MatchWidthOrHeight;

        [Header("Sprites (built-in UI/Skin)")]
        public Sprite CircleSprite;

        [Header("Match controls")]
        [Tooltip("Floating movement stick: home position from the bottom-left corner; touch zone = left half of the screen.")]
        public StickStyle MoveStick;

        [Tooltip("Attack stick, from the bottom-right corner.")]
        public StickStyle AttackStick;

        [Tooltip("Ult stick, from the bottom-right corner.")]
        public StickStyle UltStick;

        [Tooltip("Trick button centre from the bottom-right corner.")]
        public Vector2 TrickButtonPosition;

        public float TrickButtonSize;
        public Color TrickButtonColor;

        [Header("Ability state (ult charge, trick uses)")]
        [Tooltip("Ult stick base / knob while the ult is not charged (dim); charged = UltStick colours.")]
        public Color UltNotReadyBaseColor;
        public Color UltNotReadyKnobColor;

        [Tooltip("Radial fill around the ult stick showing the charge.")]
        public Color UltChargeFillColor;

        [Tooltip("Diameter of the ult charge ring, canvas units.")]
        public float UltChargeRingSize;

        [Tooltip("Trick button while on cooldown, locked or out of uses.")]
        public Color TrickButtonDisabledColor;

        [Tooltip("Dots above the trick button: one per use left.")]
        public float TrickDotSize;
        public float TrickDotSpacing;

        [Tooltip("Distance from the trick button centre up to the row of dots.")]
        public float TrickDotOffset;

        public Color TrickDotOnColor;
        public Color TrickDotOffColor;

        [Tooltip("Dead zone, cancel zone, tap time of the sticks.")]
        public StickGestureSettings Gesture;

        [Header("Team colours (D-029)")]
        public Color AllyColor;
        public Color EnemyColor;
    }
}
