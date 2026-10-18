using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.Settings
{
    /// <summary>
    /// Frame rate of the device (S2.13, GDD §8.4): vSync off, <see cref="Application.targetFrameRate"/> =
    /// <see cref="FeelConfig.TargetFrameRate"/>, or <see cref="FeelConfig.BatterySaverFrameRate"/> with the battery saver setting.
    /// Applied by the menu (start and every settings change) and by the match at its start.
    /// </summary>
    public static class FrameRateApplier
    {
        /// <summary>Frame rate for the settings (0 if <paramref name="feel"/> is null — the platform default).</summary>
        public static int FrameRateFor(GameSettings settings, FeelConfig feel)
        {
            if (feel == null) return 0;
            return settings != null && settings.BatterySaver ? feel.BatterySaverFrameRate : feel.TargetFrameRate;
        }

        public static void Apply(GameSettings settings, FeelConfig feel)
        {
            int fps = FrameRateFor(settings, feel);
            if (fps <= 0) return;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }
    }
}
