using System;

namespace Arena.Data.Localization
{
    /// <summary>One string of <see cref="LocTable"/>: key and its texts.</summary>
    [Serializable]
    public sealed class LocEntry
    {
        public string Key;
        public string En;
        public string Ru;

        public string Get(Language language) => language == Language.Ru ? Ru : En;
    }
}
