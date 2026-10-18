namespace Arena.Gameplay.Settings
{
    /// <summary>
    /// Player settings of the prototype (GDD §5.1, §5.3, D-029): mirrored control layout, vibration strength, aim magnet,
    /// colour-blind palette. Stored locally by <see cref="PlayerSettingsStore"/>. Button size and position — later (stage 5).
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>Full vibration strength, percent.</summary>
        public const int MaxVibrationPercent = 100;

        /// <summary>Movement stick on the right, attack/ult/trick on the left.</summary>
        public bool MirroredLayout;

        /// <summary>Vibration strength 0..100 %, 0 = off.</summary>
        public int VibrationPercent;

        /// <summary>Manual aim turns to a visible enemy within a few degrees (GDD §5.1: on by default).</summary>
        public bool AimMagnet;

        /// <summary>Enemies orange instead of red (allies stay blue, D-029).</summary>
        public bool ColorblindPalette;

        /// <summary>Defaults of a first start: GDD §5.1 — magnet on; full vibration; standard layout and palette.</summary>
        public static GameSettings CreateDefault() => new GameSettings
        {
            MirroredLayout = false,
            VibrationPercent = MaxVibrationPercent,
            AimMagnet = true,
            ColorblindPalette = false
        };

        /// <summary>Vibration strength as 0..1.</summary>
        public float VibrationStrength => VibrationPercent <= 0 ? 0f : VibrationPercent >= MaxVibrationPercent ? 1f
            : (float)VibrationPercent / MaxVibrationPercent;

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public bool SameAs(GameSettings o) => o != null && MirroredLayout == o.MirroredLayout && VibrationPercent == o.VibrationPercent
            && AimMagnet == o.AimMagnet && ColorblindPalette == o.ColorblindPalette;
    }
}
