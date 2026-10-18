using System;
using System.Collections.Generic;

namespace Arena.Gameplay.Settings
{
    /// <summary>In-memory <see cref="IKeyValueStore"/> (tests, previews). <see cref="SaveCount"/> counts <see cref="Save"/> calls.</summary>
    public sealed class MemoryKeyValueStore : IKeyValueStore
    {
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>(StringComparer.Ordinal);

        public int SaveCount { get; private set; }

        public bool HasKey(string key) => _ints.ContainsKey(key);
        public int GetInt(string key, int fallback) => _ints.TryGetValue(key, out int v) ? v : fallback;
        public void SetInt(string key, int value) => _ints[key] = value;
        public void Save() => SaveCount++;
    }
}
