using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.Data.Localization
{
    /// <summary>
    /// String table of the UI (D-019): key → EN / RU. Written by DataSeeder (<c>Data/Localization/Strings.asset</c>);
    /// read through <see cref="Loc"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Strings", menuName = "Arena/Localization Table")]
    public sealed class LocTable : ScriptableObject
    {
        public List<LocEntry> Entries = new List<LocEntry>();

        /// <summary>Entry by key (linear search; <see cref="Loc"/> builds a dictionary for runtime use), or null.</summary>
        public LocEntry Find(string key)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i] != null && string.Equals(Entries[i].Key, key, StringComparison.Ordinal)) return Entries[i];
            return null;
        }
    }
}
