using Arena.Data;

namespace Arena.Gameplay.Settings
{
    /// <summary>
    /// Player settings of the prototype (GDD §5.1, §5.3, D-029): mirrored control layout, vibration strength, sound volume, aim magnet,
    /// colour-blind palette, battery saver (30 FPS, S2.13). Stored locally by <see cref="PlayerSettingsStore"/>.
    /// Defaults of a first start come from <see cref="FeelConfig"/> (no magic numbers in code, D-062). Button size and
    /// position — later (stage 5).
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>Full vibration strength (and sound volume), percent (the scale of the setting, not a tunable value).</summary>
        public const int MaxVibrationPercent = 100;

        /// <summary>Movement stick on the right, attack/ult/trick on the left.</summary>
        public bool MirroredLayout;

        /// <summary>Vibration strength 0..100 %, 0 = off.</summary>
        public int VibrationPercent;

        /// <summary>Sound volume 0..100 %, 0 = silent (brief A-TheoSfx).</summary>
        public int SoundPercent;

        /// <summary>Manual aim turns to a visible enemy within a few degrees (GDD §5.1: on by default).</summary>
        public bool AimMagnet;

        /// <summary>Enemies orange instead of red (allies stay blue, D-029).</summary>
        public bool ColorblindPalette;

        /// <summary>Battery saver: the frame rate is capped at <see cref="FeelConfig.BatterySaverFrameRate"/> (S2.13).</summary>
        public bool BatterySaver;

        /// <summary>Defaults of a first start from the data (<see cref="FeelConfig"/> "Settings defaults"); all off if null.</summary>
        public static GameSettings FromDefaults(FeelConfig feel)
        {
            if (feel == null) return new GameSettings();
            return new GameSettings
            {
                MirroredLayout = feel.DefaultMirroredLayout,
                VibrationPercent = Clamp(feel.DefaultVibrationPercent),
                SoundPercent = Clamp(feel.DefaultSoundPercent),
                AimMagnet = feel.DefaultAimMagnet,
                ColorblindPalette = feel.DefaultColorblindPalette,
                BatterySaver = feel.DefaultBatterySaver
            };
        }

        /// <summary>Vibration strength as 0..1.</summary>
        public float VibrationStrength => VibrationPercent <= 0 ? 0f : VibrationPercent >= MaxVibrationPercent ? 1f
            : (float)VibrationPercent / MaxVibrationPercent;

        /// <summary>Sound volume as 0..1.</summary>
        public float SoundVolume => SoundPercent <= 0 ? 0f : SoundPercent >= MaxVibrationPercent ? 1f
            : (float)SoundPercent / MaxVibrationPercent;

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public bool SameAs(GameSettings o) => o != null && MirroredLayout == o.MirroredLayout && VibrationPercent == o.VibrationPercent
            && SoundPercent == o.SoundPercent
            && AimMagnet == o.AimMagnet && ColorblindPalette == o.ColorblindPalette && BatterySaver == o.BatterySaver;

        /// <summary>Clamps a vibration (or sound) percent to 0..<see cref="MaxVibrationPercent"/>.</summary>
        public static int Clamp(int percent) => percent < 0 ? 0 : percent > MaxVibrationPercent ? MaxVibrationPercent : percent;
    }
}
