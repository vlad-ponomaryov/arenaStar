using System;
using Arena.Data;

namespace Arena.Gameplay.Settings
{
    /// <summary>
    /// Loads and saves <see cref="GameSettings"/> through an <see cref="IKeyValueStore"/> (PlayerPrefs on the device — local only,
    /// no data leaves it). Missing keys take the defaults (from <see cref="FeelConfig"/>, D-062); out-of-range values are clamped.
    /// <see cref="Shared"/> is the process-wide store over PlayerPrefs used by the menu (settings panel) and the match (runner,
    /// HUD); <see cref="SharedFor"/> gives it the data defaults first.
    /// </summary>
    public sealed class PlayerSettingsStore
    {
        public const string MirroredLayoutKey = "arena.settings.mirrored";
        public const string VibrationKey = "arena.settings.vibration";
        public const string SoundKey = "arena.settings.sound";
        public const string AimMagnetKey = "arena.settings.aim_magnet";
        public const string ColorblindKey = "arena.settings.colorblind";
        public const string BatterySaverKey = "arena.settings.battery_saver";

        private static PlayerSettingsStore _shared;

        private readonly IKeyValueStore _store;
        private GameSettings _defaults;
        private GameSettings _current;

        /// <param name="defaults">Values for missing keys (first start); null = all off.</param>
        public PlayerSettingsStore(IKeyValueStore store, GameSettings defaults = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _defaults = defaults != null ? defaults.Clone() : new GameSettings();
        }

        /// <summary>Store over PlayerPrefs, created on first use (defaults: see <see cref="SharedFor"/>).</summary>
        public static PlayerSettingsStore Shared => _shared ?? (_shared = new PlayerSettingsStore(new PlayerPrefsKeyValueStore()));

        /// <summary><see cref="Shared"/> with the first-start defaults of <paramref name="feel"/> (call before reading it).</summary>
        public static PlayerSettingsStore SharedFor(FeelConfig feel)
        {
            PlayerSettingsStore s = Shared;
            if (feel != null) s.SetDefaults(GameSettings.FromDefaults(feel));
            return s;
        }

        /// <summary>Defaults for missing keys.</summary>
        public GameSettings Defaults => _defaults.Clone();

        /// <summary>Changes the defaults; <see cref="Current"/> is re-read if they differ.</summary>
        public void SetDefaults(GameSettings defaults)
        {
            if (defaults == null || defaults.SameAs(_defaults)) return;
            _defaults = defaults.Clone();
            _current = null;
        }

        /// <summary>Settings in use (loaded on first access). Do not edit in place: change a copy and <see cref="Save"/> it.</summary>
        public GameSettings Current => _current ?? (_current = Load());

        /// <summary>Raised after <see cref="Save"/>.</summary>
        public event Action<GameSettings> Changed;

        /// <summary>Reads the settings from the store (defaults for missing keys).</summary>
        public GameSettings Load()
        {
            GameSettings d = _defaults;
            var s = new GameSettings
            {
                MirroredLayout = ReadBool(MirroredLayoutKey, d.MirroredLayout),
                VibrationPercent = GameSettings.Clamp(_store.GetInt(VibrationKey, d.VibrationPercent)),
                SoundPercent = GameSettings.Clamp(_store.GetInt(SoundKey, d.SoundPercent)),
                AimMagnet = ReadBool(AimMagnetKey, d.AimMagnet),
                ColorblindPalette = ReadBool(ColorblindKey, d.ColorblindPalette),
                BatterySaver = ReadBool(BatterySaverKey, d.BatterySaver)
            };
            return s;
        }

        /// <summary>Writes <paramref name="settings"/>, makes a copy of them <see cref="Current"/> and raises <see cref="Changed"/>.</summary>
        public void Save(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _store.SetInt(MirroredLayoutKey, settings.MirroredLayout ? 1 : 0);
            _store.SetInt(VibrationKey, GameSettings.Clamp(settings.VibrationPercent));
            _store.SetInt(SoundKey, GameSettings.Clamp(settings.SoundPercent));
            _store.SetInt(AimMagnetKey, settings.AimMagnet ? 1 : 0);
            _store.SetInt(ColorblindKey, settings.ColorblindPalette ? 1 : 0);
            _store.SetInt(BatterySaverKey, settings.BatterySaver ? 1 : 0);
            _store.Save();
            _current = Load();
            Changed?.Invoke(_current);
        }

        private bool ReadBool(string key, bool fallback) => _store.GetInt(key, fallback ? 1 : 0) != 0;
    }
}
