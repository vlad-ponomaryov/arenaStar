namespace Arena.Gameplay.Settings
{
    /// <summary>Local key-value storage of settings (PlayerPrefs on the device, memory in tests). Keeps data on the device only.</summary>
    public interface IKeyValueStore
    {
        bool HasKey(string key);
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        void DeleteKey(string key);

        /// <summary>Writes pending changes to disk.</summary>
        void Save();
    }
}
