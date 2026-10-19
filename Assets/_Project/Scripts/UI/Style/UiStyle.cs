using Arena.Net.Client;
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
        [Tooltip("Ult charge ring track / ult button while the ult is not charged (dim); charged = UltStick colours.")]
        public Color UltNotReadyBaseColor;
        public Color UltNotReadyKnobColor;

        [Tooltip("Trick button while on cooldown, locked or out of uses.")]
        public Color TrickButtonDisabledColor;

        [Tooltip("Acorn beads above the trick button: one per use left (A.7a: AcornIcon, tinted On / Off).")]
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

        [Header("Match menu (pause / leave, D-129)")]
        [Tooltip("Pause button of the match HUD: offset from the top-right corner and size.")]
        public Vector2 PauseButtonMargin;
        public Vector2 PauseButtonSize;

        [Tooltip("Bars of the pause icon: size of one bar and the gap between them.")]
        public Vector2 PauseIconBarSize;
        public float PauseIconGap;

        [Tooltip("Buttons of the match menu panel (Resume, Settings, Leave; Leave / Stay).")]
        public Vector2 PauseMenuButtonSize;

        [Tooltip("Width of the title plank and the text lines of the match menu panel.")]
        public float PauseMenuWidth;

        [Header("Settings panel")]
        public Color SettingsPanelColor;

        [Tooltip("Width of the label column of a settings row.")]
        public float SettingsLabelWidth;

        [Tooltip("Size of one choice button (On/Off, vibration steps).")]
        public Vector2 SettingsChoiceSize;

        [Header("Text and buttons (menu, score, results; Nunito since A.6, D-018)")]
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

        [Tooltip("Nunito Bold (A.6, SIL OFL 1.1): body text. Null — the built-in LegacyRuntime font.")]
        public Font Font;

        [Tooltip("Nunito ExtraBold: titles and numbers (font size ≥ LargeFontSize). Null — Font.")]
        public Font HeadingFont;

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
        [Header("Online (S3.11): ping badge, connection overlay, matchmaking screen")]
        [Tooltip("Ping badge colours (GDD §8.2): green below PingGoodMs, yellow below PingFairMs, red from it; grey — unknown.")]
        public Color PingGoodColor;
        public Color PingFairColor;
        public Color PingPoorColor;
        public Color PingUnknownColor;

        [Tooltip("Ping badge of the match HUD: offset below the FPS counter (top-left) and size.")]
        public Vector2 PingMargin;
        public Vector2 PingBoxSize;

        [Tooltip("Connection overlay of the match HUD (\"connection lost\", \"a bot is playing\"): panel colour and size, from the top.")]
        public Color OverlayPanelColor;
        public Vector2 OverlayBoxSize;
        public float OverlayTopMargin;

        [Tooltip("Matchmaking screen over the menu.")]
        public Color MatchmakingBackdropColor;

        [Header("Wood and leaves skin (A.6, Art-Spec §1.3, §4.2): sprites baked by UiSpriteBaker")]
        [Tooltip("Cream panel with a wooden frame (9-slice, coloured in the sprite) and its colours for the baker.")]
        public Sprite PanelSprite;
        public Color PanelColor;
        public Color PanelBorderColor;

        [Tooltip("Rounded white shape with a dark rim (9-slice): the bottom edge of a button, tinted by the edge colour.")]
        public Sprite ButtonEdgeSprite;

        [Tooltip("Rounded white face with a soft vertical shade (9-slice), tinted by the button colour.")]
        public Sprite ButtonFaceSprite;

        [Tooltip("White gloss over the top of a button face (9-slice).")]
        public Sprite ButtonGlossSprite;

        [Tooltip("Soft blurred rounded shape (9-slice): drop shadow of buttons and panels.")]
        public Sprite ShadowSprite;

        [Tooltip("Wooden plank with grain (9-slice, coloured): title of a panel.")]
        public Sprite PlankSprite;

        [Tooltip("Leaf in two greens with a dark outline: corners of the main panels.")]
        public Sprite LeafSprite;

        [Tooltip("Full-screen menu background (meadow gradient with faint leaves).")]
        public Sprite BackdropSprite;

        [Tooltip("Pixels-per-unit multiplier of the sliced button/panel sprites (bigger — smaller corners).")]
        public float SliceScale;

        [Tooltip("Height of the dark bottom edge under a button face, canvas units; pressing moves the face down onto it.")]
        public float ButtonEdgeHeight;

        [Tooltip("Rim of the edge sprite around the face (left, right, top), canvas units; the baker draws it at SliceScale.")]
        public float ButtonOutline;

        [Tooltip("Share of the edge height the face moves down while pressed.")]
        [Range(0f, 1f)] public float ButtonPressDepth;

        [Tooltip("Edge colour of a button colour without its own edge (choices): face colour × this.")]
        [Range(0f, 1f)] public float ButtonEdgeShade;

        public Color PrimaryButtonEdgeColor;

        [Tooltip("Secondary action and the chosen option (sky blue) and its edge.")]
        public Color SecondaryButtonColor;
        public Color SecondaryButtonEdgeColor;

        [Tooltip("Gloss strength over button faces (alpha of the white gloss).")]
        [Range(0f, 1f)] public float ButtonGlossAlpha;

        public Color ShadowColor;

        [Tooltip("Drop shadow offset of buttons and panels, canvas units.")]
        public Vector2 ShadowOffset;

        [Tooltip("Dark text on cream panels and light buttons; outline of light text (HUD, coloured buttons).")]
        public Color TextDarkColor;
        public Color TextOutlineColor;
        public Vector2 TextOutlineDistance;

        [Tooltip("Faces brighter than this (luma) get dark text, others white text with the outline.")]
        [Range(0f, 1f)] public float LightFaceLuma;

        [Tooltip("Inner padding of a panel (left/right, top/bottom), canvas units.")]
        public Vector2 PanelPadding;

        public Vector2 LeafSize;

        [Tooltip("Height of the plank title of a panel.")]
        public float PlankHeight;

        [Tooltip("Title colour of the results (gold) and of a defeat.")]
        public Color VictoryTitleColor;
        public Color DefeatTitleColor;

        [Tooltip("Every second row of the results table.")]
        public Color RowStripeColor;

        [Tooltip("Menu background gradient (baker): top / bottom, and the faint leaves.")]
        public Color BackdropTopColor;
        public Color BackdropBottomColor;
        public Color BackdropLeafColor;

        [Tooltip("Pixels-per-unit multiplier of the panel sprite (smaller than SliceScale — a thicker wooden frame).")]
        public float PanelSliceScale;

        [Tooltip("Button labels sit this share of the font size lower than the face centre (optical centring of Nunito).")]
        public float LabelBaselineShift;

        [Tooltip("Width of the label column of a menu row (labels line up on the board).")]
        public float MenuLabelWidth;

        [Tooltip("Label alpha of a button that is not interactable (its gloss is hidden, the face greyed).")]
        [Range(0f, 1f)] public float DisabledLabelAlpha;

        [Header("Match HUD skin (A.7a, Art-Spec §4.1–4.2): baked sprites and icons")]
        [Tooltip("Stick base: translucent disc with a light rim and a dark outline (white, tinted by StickStyle.BaseColor).")]
        public Sprite StickBaseSprite;

        [Tooltip("Toy knob / round button: dark outline, shaded face, gloss on top (white, tinted).")]
        public Sprite KnobSprite;

        [Tooltip("A.7c: thin ult charge ring around the ult button (white band with a dark outline; filled radially by the charge).")]
        public Sprite UltRingSprite;

        [Tooltip("Soft radial glow (white, tinted): behind the charged ult.")]
        public Sprite GlowSprite;

        [Tooltip("Service icons (IconBaker, flat two-tone with the dark outline, 128 px): acorn, paw, leaf medal, defeat stars.")]
        public Sprite AcornIcon;
        public Sprite PawIcon;
        public Sprite LeafMedalIcon;
        public Sprite StarsIcon;

        [Tooltip("Ult and trick icons shown on the HUD buttons (generic until the per-fighter 3D icons, D-134).")]
        public Sprite UltIcon;
        public Sprite TrickIcon;

        [Tooltip("A.7c ult button: the charge ring fills clockwise from the top in this colour (its track = UltNotReadyBaseColor, "
            + "the full ring of the ready ult = UltStick.BaseColor).")]
        public Color UltRingFillColor;

        [Tooltip("A.7c attack stick while pressed / aiming: a light accent over the translucent base (at rest = AttackStick colours).")]
        public Color AttackHeldBaseColor;
        public Color AttackHeldKnobColor;

        [Tooltip("Golden glow behind the ready ult: size and colour (alpha pulses with the ult glow).")]
        public float UltGlowSize;
        public Color UltGlowColor;

        [Tooltip("Icon on the ult knob and on the trick button: size; tint while ready / not ready.")]
        public float UltIconSize;
        public float TrickIconSize;
        public Color IconReadyColor;
        public Color IconNotReadyColor;

        [Header("Score bar and Last Beast panel skin (A.7b)")]
        [Tooltip("Acorn icon of a score tablet: its height as a share of the tablet height, and its margin from the left edge.")]
        [Range(0f, 1f)] public float ScoreIconShare;
        public float ScoreIconMargin;

        public Color Enemy(bool colorblind) => colorblind ? EnemyColorAlt : EnemyColor;

        /// <summary>Bottom edge colour of a button face colour: the palette pair for primary/secondary, else a darker shade.</summary>
        public Color EdgeFor(Color face)
        {
            if (Same(face, PrimaryButtonColor)) return PrimaryButtonEdgeColor;
            if (Same(face, SecondaryButtonColor)) return SecondaryButtonEdgeColor;
            return new Color(face.r * ButtonEdgeShade, face.g * ButtonEdgeShade, face.b * ButtonEdgeShade, face.a);
        }

        /// <summary>Text on a face of this colour: dark on light faces, otherwise light with the outline.</summary>
        public bool IsLightFace(Color face) => 0.299f * face.r + 0.587f * face.g + 0.114f * face.b > LightFaceLuma;

        private static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;

        public Color Ping(PingQuality quality)
        {
            switch (quality)
            {
                case PingQuality.Good: return PingGoodColor;
                case PingQuality.Fair: return PingFairColor;
                case PingQuality.Poor: return PingPoorColor;
                default: return PingUnknownColor;
            }
        }
    }
}
