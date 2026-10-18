using System.Collections.Generic;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Local match loop and the default (prototype) match setup: who sits in which slot and where the temporary spawns are
    /// (until map spawn points of S2.5). Values are written by DataSeeder (D-010).
    /// </summary>
    [CreateAssetMenu(fileName = "MatchRunnerConfig", menuName = "Arena/Match Runner Config")]
    public sealed class MatchRunnerConfig : ScriptableObject
    {
        [Tooltip("Max simulation ticks per rendered frame; the remaining time is dropped (protects against a spiral of death).")]
        [Min(1)] public int MaxTicksPerFrame;

        [Tooltip("Seed of the match RNG.")]
        public int Seed;

        [Tooltip("Index of the slot controlled by the local player.")]
        [Min(0)] public int LocalSlot;

        [Tooltip("Fighter id per slot. Slots [0, TeamSize) are team 0, the rest team 1.")]
        public List<string> SlotFighterIds = new List<string>();

        [Min(1)] public int TeamSize;

        [Min(1)] public int FighterLevel;

        [Header("Temporary spawns (until map spawns)")]
        [Tooltip("Distance of the spawn row from the team's own short edge of the arena, cells.")]
        [Min(0f)] public float SpawnEdgeInset;

        [Tooltip("Distance between neighbouring spawns of a team across the arena, cells.")]
        [Min(0f)] public float SpawnSpacing;
    }
}
