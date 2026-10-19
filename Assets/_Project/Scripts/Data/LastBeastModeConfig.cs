using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// The «Last Beast» mode (GDD §5.4, Server-Spec §9) in designer units: seconds, cells, health. Converted to
    /// <c>LastBeastModeDef</c> by ConfigConverter. Values are written by DataSeeder (D-010); numbers outside the GDD are PROTO (D-089).
    /// </summary>
    [CreateAssetMenu(fileName = "LastBeast", menuName = "Arena/Last Beast Mode")]
    public sealed class LastBeastModeConfig : ScriptableObject
    {
        [Header("Fog")]
        [Tooltip("The fog starts moving in at … seconds (GDD: 0:40).")]
        [Min(0f)] public float FogStartSeconds;

        [Tooltip("Then it moves in every … seconds (GDD: 20).")]
        [Min(0.1f)] public float FogStepIntervalSeconds;

        [Tooltip("Each step moves every side of the safe square in by … cells (GDD: 2).")]
        [Min(0.1f)] public float FogShrinkCells;

        [Tooltip("The safe square never gets smaller than this half size, cells.")]
        [Min(0f)] public float FogMinHalfSize;

        [Tooltip("Fog damage per second at the first step (GDD: 500).")]
        [Min(0)] public int FogDamagePerSecond;

        [Tooltip("Fog damage per second added with every further step.")]
        [Min(0)] public int FogDamageGrowthPerStep;

        [Tooltip("Fog damage is dealt in pulses every … seconds.")]
        [Min(0.05f)] public float FogPulseSeconds;

        [Header("Boxes and berries")]
        [Min(1)] public int BoxHealth;

        [Min(0)] public int BerriesPerBox;

        [Tooltip("Damage bonus per berry, percent, additive (GDD: 8).")]
        [Min(0)] public int BerryDamagePercent;

        [Tooltip("Max health bonus per berry, percent, additive (GDD: 8).")]
        [Min(0)] public int BerryHealthPercent;

        [Tooltip("Berries counted for the bonuses (D-131: a cap against a runaway leader); 0 — no cap.")]
        [Min(0)] public int MaxBonusBerries;

        [Tooltip("Health restored per picked berry, percent of the new max health (D-131).")]
        [Min(0)] public int BerryHealPercent;

        [Tooltip("A defeated fighter drops all its berries and … more (GDD: 1).")]
        [Min(0)] public int ExtraBerriesOnDefeat;

        [Tooltip("Berry radius, cells: picked up when the body touches it.")]
        [Min(0f)] public float BerryPickupRadius;

        [Tooltip("Dropped berries land within this radius, cells.")]
        [Min(0f)] public float BerryDropScatterRadius;

        [Tooltip("Dropped berries cannot be picked up for … seconds.")]
        [Min(0f)] public float BerryPickupDelaySeconds;

        [Tooltip("Random landing points tried before falling back to the centre (walls, water).")]
        [Min(1)] public int ScatterAttempts;

        [Header("End")]
        [Tooltip("Safety limit of the match, seconds: then the winner is chosen by health, then by damage.")]
        [Min(1f)] public float MaxDurationSeconds;
    }
}
