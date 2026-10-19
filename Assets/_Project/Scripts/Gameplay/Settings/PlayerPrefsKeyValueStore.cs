using UnityEngine;

namespace Arena.Gameplay.Settings
{
    /// <summary><see cref="IKeyValueStore"/> over <see cref="PlayerPrefs"/> (local device storage, no data leaves the device).</summary>
    public sealed class PlayerPrefsKeyValueStore : IKeyValueStore
    {
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public void Save() => PlayerPrefs.Save();
    }
}
