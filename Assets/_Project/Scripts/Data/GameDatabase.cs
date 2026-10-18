using System;
using System.Collections.Generic;
using Arena.Bots;
using Arena.Data.Localization;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Root of all game configs (D-010); scenes reference it directly. Filled by DataSeeder.
    /// </summary>
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "Arena/Game Database")]
    public sealed class GameDatabase : ScriptableObject
    {
        public CombatRulesConfig CombatRules;
        public CameraConfig Camera;

        [Tooltip("Hit feedback, sounds, vibration, telegraph, aim magnet (GDD §5.3).")]
        public FeelConfig Feel;
        public MatchRunnerConfig MatchRunner;

        [Tooltip("All fighters in menu order.")]
        public List<FighterConfig> Fighters = new List<FighterConfig>();

        [Tooltip("Map of the prototype match (Acorns).")]
        public MapConfig DefaultMap;

        [Tooltip("Rules of the Acorns mode (the stage-2 match). Empty = free fight without an objective.")]
        public AcornsModeConfig AcornsMode;

        [Tooltip("UI strings EN/RU (D-019).")]
        public LocTable Strings;

        [Tooltip("Bot skill levels offered in the menu (GDD §7).")]
        public List<BotDifficultyConfig> BotDifficulties = new List<BotDifficultyConfig>();

        [Tooltip("Bot behaviour shared by all difficulties.")]
        public BotBehaviourConfig BotBehaviour;

        /// <summary>Fighter config by id, or null.</summary>
        public FighterConfig FindFighter(string id)
        {
            for (int i = 0; i < Fighters.Count; i++)
                if (Fighters[i] != null && string.Equals(Fighters[i].Id, id, StringComparison.Ordinal)) return Fighters[i];
            return null;
        }

        /// <summary>Bot difficulty config of <paramref name="difficulty"/>, or null.</summary>
        public BotDifficultyConfig FindBotDifficulty(BotDifficulty difficulty)
        {
            for (int i = 0; i < BotDifficulties.Count; i++)
                if (BotDifficulties[i] != null && BotDifficulties[i].Difficulty == difficulty) return BotDifficulties[i];
            return null;
        }
    }
}
