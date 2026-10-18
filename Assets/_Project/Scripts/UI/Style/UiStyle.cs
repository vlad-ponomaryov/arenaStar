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

        [Tooltip("Dead zone, cancel zone, tap time of the sticks.")]
        public StickGestureSettings Gesture;

        [Header("Team colours (D-029)")]
        public Color AllyColor;
        public Color EnemyColor;
    }
}
