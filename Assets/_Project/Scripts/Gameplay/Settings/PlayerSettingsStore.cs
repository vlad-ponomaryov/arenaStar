using System;

namespace Arena.Gameplay.Settings
{
    /// <summary>
    /// Loads and saves <see cref="GameSettings"/> through an <see cref="IKeyValueStore"/> (PlayerPrefs on the device — local only,
    /// no data leaves it). Missing keys take the defaults; out-of-range values are clamped. <see cref="Shared"/> is the
    /// process-wide store over PlayerPrefs used by the menu (settings panel) and the match (runner, HUD).
    /// </summary>
    public sealed class PlayerSettingsStore
    {
        public const string MirroredLayoutKey = "arena.settings.mirrored";
        public const string VibrationKey = "arena.settings.vibration";
        public const string AimMagnetKey = "arena.settings.aim_magnet";
        public const string ColorblindKey = "arena.settings.colorblind";

        private static PlayerSettingsStore _shared;

        private readonly IKeyValueStore _store;
        private GameSettings _current;

        public PlayerSettingsStore(IKeyValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Store over PlayerPrefs, created on first use.</summary>
        public static PlayerSettingsStore Shared => _shared ?? (_shared = new PlayerSettingsStore(new PlayerPrefsKeyValueStore()));

        /// <summary>Settings in use (loaded on first access). Do not edit in place: change a copy and <see cref="Save"/> it.</summary>
        public GameSettings Current => _current ?? (_current = Load());

        /// <summary>Raised after <see cref="Save"/>.</summary>
        public event Action<GameSettings> Changed;

        /// <summary>Reads the settings from the store (defaults for missing keys).</summary>
        public GameSettings Load()
        {
            GameSettings d = GameSettings.CreateDefault();
            var s = new GameSettings
            {
                MirroredLayout = ReadBool(MirroredLayoutKey, d.MirroredLayout),
                VibrationPercent = Clamp(_store.GetInt(VibrationKey, d.VibrationPercent), 0, GameSettings.MaxVibrationPercent),
                AimMagnet = ReadBool(AimMagnetKey, d.AimMagnet),
                ColorblindPalette = ReadBool(ColorblindKey, d.ColorblindPalette)
            };
            return s;
        }

        /// <summary>Writes <paramref name="settings"/>, makes a copy of them <see cref="Current"/> and raises <see cref="Changed"/>.</summary>
        public void Save(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _store.SetInt(MirroredLayoutKey, settings.MirroredLayout ? 1 : 0);
            _store.SetInt(VibrationKey, Clamp(settings.VibrationPercent, 0, GameSettings.MaxVibrationPercent));
            _store.SetInt(AimMagnetKey, settings.AimMagnet ? 1 : 0);
            _store.SetInt(ColorblindKey, settings.ColorblindPalette ? 1 : 0);
            _store.Save();
            _current = Load();
            Changed?.Invoke(_current);
        }

        private bool ReadBool(string key, bool fallback) => _store.GetInt(key, fallback ? 1 : 0) != 0;

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }
}
