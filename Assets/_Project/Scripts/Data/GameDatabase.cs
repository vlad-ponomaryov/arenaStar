using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Root of all game configs (D-010); scenes reference it directly. Filled by DataSeeder.
    /// Default map, modes, bots, feel come in later steps.
    /// </summary>
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "Arena/Game Database")]
    public sealed class GameDatabase : ScriptableObject
    {
        public CombatRulesConfig CombatRules;

        [Tooltip("All fighters in menu order.")]
        public List<FighterConfig> Fighters = new List<FighterConfig>();

        [Tooltip("Temporary rectangular arena size in cells (width × height) until grid maps (S2.5).")]
        public Vector2 TempArenaSize;

        /// <summary>Fighter config by id, or null.</summary>
        public FighterConfig FindFighter(string id)
        {
            for (int i = 0; i < Fighters.Count; i++)
                if (Fighters[i] != null && string.Equals(Fighters[i].Id, id, StringComparison.Ordinal)) return Fighters[i];
            return null;
        }
    }
}
