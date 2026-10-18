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

        [Tooltip("Enemy colour of the colour-blind palette (orange; allies stay blue).")]
        public Color EnemyColorAlt;

        [Header("FPS counter (top-left corner)")]
        public Vector2 FpsMargin;
        public Vector2 FpsBoxSize;

        [Header("Settings panel")]
        public Color SettingsPanelColor;

        [Tooltip("Width of the label column of a settings row.")]
        public float SettingsLabelWidth;

        [Tooltip("Size of one choice button (On/Off, vibration steps).")]
        public Vector2 SettingsChoiceSize;

        [Header("Text and buttons (menu, score, results; built-in LegacyRuntime font, D-018)")]
        public Color TextColor;
        public int TitleFontSize;
        public int LargeFontSize;
        public int MediumFontSize;
        public int SmallFontSize;
        public Color ButtonColor;

        [Tooltip("Chosen option in a row of choices (fighter, bots, language).")]
        public Color ButtonSelectedColor;

        [Tooltip("Main action: Play, Play again.")]
        public Color PrimaryButtonColor;

        [Header("Menu")]
        public Vector2 MenuButtonSize;
        public Vector2 PlayButtonSize;

        [Tooltip("Vertical gap between the rows of the menu.")]
        public float MenuRowSpacing;

        [Tooltip("Horizontal gap between the buttons of a row.")]
        public float MenuItemSpacing;

        [Tooltip("Version label offset from the bottom-left corner.")]
        public Vector2 VersionMargin;

        [Header("Score bar (Acorns)")]
        [Tooltip("Distance from the top edge of the screen to the bar.")]
        public float ScoreBarTopMargin;

        public Vector2 ScoreBoxSize;
        public Vector2 TimerBoxSize;
        public float ScoreBarSpacing;
        public Color ScorePanelColor;

        [Tooltip("Hold countdown text below the bar.")]
        public Vector2 CountdownSize;

        [Header("Results")]
        public Color ResultsBackdropColor;
        public Vector2 MvpCapsuleSize;

        [Tooltip("Jump height of the MVP capsule, canvas units, and jumps per second.")]
        public float MvpBounceHeight;
        public float MvpBounceFrequency;

        public float ResultsRowHeight;
        public float ResultsNameColumnWidth;
        public float ResultsStatColumnWidth;
        public float ResultsSpacing;
        public Vector2 ResultsButtonSize;

        /// <summary>Enemy colour of the chosen palette (D-029).</summary>
        public Color Enemy(bool colorblind) => colorblind ? EnemyColorAlt : EnemyColor;
    }
}
