using System;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Common combat rules (GDD §5.2) in designer units: seconds, fractions, cells per second.
    /// Converted to <c>Arena.Core.Defs.CombatRules</c> by ConfigConverter. Values are written by DataSeeder (D-010).
    /// </summary>
    [CreateAssetMenu(fileName = "CombatRules", menuName = "Arena/Combat Rules")]
    public sealed class CombatRulesConfig : ScriptableObject
    {
        [Header("Attack charges")]
        [Min(1)] public int MaxCharges;

        [Header("Regeneration")]
        [Tooltip("Seconds without attacking or taking damage before regeneration starts.")]
        [Min(0f)] public float RegenDelaySeconds;

        [Tooltip("Fraction of max health regenerated per second (0.12 = 12 %).")]
        [Min(0f)] public float RegenFractionPerSecond;

        [Header("Respawn")]
        [Tooltip("Seconds from defeat to respawn in team modes.")]
        [Min(0f)] public float RespawnSeconds;

        [Header("Levels")]
        [Tooltip("Health and damage bonus per level above 1 (0.06 = +6 %).")]
        [Min(0f)] public float LevelBonusPerLevel;

        [Header("Ult, trick, talent")]
        [Tooltip("Fraction of the ult charge lost on defeat (0.5 = half).")]
        [Range(0f, 1f)] public float UltLossOnDeath;

        [Tooltip("Trick uses per match.")]
        [Min(0)] public int TrickUses;

        [Tooltip("Seconds between trick uses.")]
        [Min(0f)] public float TrickCooldownSeconds;

        [Tooltip("Fighter level that opens the trick (unless the match unlocks all, D-012).")]
        [Min(1)] public int TrickUnlockLevel = 1;

        [Tooltip("Fighter level that opens the talent (unless the match unlocks all, D-012).")]
        [Min(1)] public int TalentUnlockLevel = 1;

        [Header("Movement speed by class, cells per second")]
        [Min(0f)] public float SlowSpeed;
        [Min(0f)] public float NormalSpeed;
        [Min(0f)] public float FastSpeed;

        public float SpeedFor(SpeedClass speedClass)
        {
            switch (speedClass)
            {
                case SpeedClass.Slow: return SlowSpeed;
                case SpeedClass.Normal: return NormalSpeed;
                case SpeedClass.Fast: return FastSpeed;
                default: throw new ArgumentOutOfRangeException(nameof(speedClass), speedClass, "Unknown speed class");
            }
        }
    }
}
