using System.Collections.Generic;
using Arena.Bots;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Local match loop and the default (prototype) match setup: who sits in which slot (spawns come from the map); all slots
    /// except <see cref="LocalSlot"/> are bots.
    /// Values are written by DataSeeder (D-010).
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

        [Tooltip("Trick and talent open regardless of level (D-012: on in the prototype).")]
        public bool UnlockAllAbilities = true;

        [Tooltip("Bot difficulty when the Match scene is opened directly (no menu choice).")]
        public BotDifficulty DefaultBots;

        [Tooltip("Bots play random fighters (match seed, no repeats within a team) instead of the slot line-up.")]
        public bool RandomBotFighters = true;
    }
}
