using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.Data.Localization
{
    /// <summary>
    /// Static access to the UI strings (D-019): <c>Loc.Get(LocKeys.MenuPlay)</c>. <see cref="Init(LocTable)"/> is called by
    /// every screen with the table from <c>GameDatabase.Strings</c> (repeated calls with the same table are free). The
    /// language is a local setting in <see cref="PlayerPrefs"/> (no data leaves the device); the first start picks RU for
    /// a Russian system language, otherwise EN. A missing key returns the key itself, so it is visible on screen.
    /// </summary>
    public static class Loc
    {
        public const string LanguagePrefsKey = "arena.language";

        private static readonly Dictionary<string, LocEntry> Map = new Dictionary<string, LocEntry>(StringComparer.Ordinal);
        private static LocTable _table;

        public static Language Current { get; private set; }

        public static bool IsInitialized => _table != null;

        /// <summary>Raised after <see cref="SetLanguage"/> changes the language (screens rebuild their texts).</summary>
        public static event Action LanguageChanged;

        /// <summary>Uses <paramref name="table"/> and the saved (or system) language.</summary>
        public static void Init(LocTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (ReferenceEquals(table, _table)) return;
            Init(table, LoadLanguage());
        }

        /// <summary>Uses <paramref name="table"/> and <paramref name="language"/> without touching PlayerPrefs (tests).</summary>
        public static void Init(LocTable table, Language language)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            _table = table;
            Map.Clear();
            for (int i = 0; i < table.Entries.Count; i++)
            {
                LocEntry e = table.Entries[i];
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                Map[e.Key] = e;
            }
            Current = language;
        }

        public static bool Has(string key) => key != null && Map.ContainsKey(key);

        /// <summary>Text of <paramref name="key"/> in the current language; the key itself if missing or empty.</summary>
        public static string Get(string key)
        {
            if (key == null) return string.Empty;
            if (!Map.TryGetValue(key, out LocEntry e)) return key;
            string text = e.Get(Current);
            return string.IsNullOrEmpty(text) ? key : text;
        }

        /// <summary>Switches and saves the language.</summary>
        public static void SetLanguage(Language language)
        {
            if (language == Current) return;
            Current = language;
            PlayerPrefs.SetInt(LanguagePrefsKey, (int)language);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke();
        }

        private static Language LoadLanguage()
        {
            if (PlayerPrefs.HasKey(LanguagePrefsKey))
            {
                int saved = PlayerPrefs.GetInt(LanguagePrefsKey);
                if (saved == (int)Language.En || saved == (int)Language.Ru) return (Language)saved;
            }
            return Application.systemLanguage == SystemLanguage.Russian ? Language.Ru : Language.En;
        }
    }
}
