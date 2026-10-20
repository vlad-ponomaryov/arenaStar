using System;
using System.Collections.Generic;
using Arena.Bots;
using Arena.Data.Audio;
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

        [Tooltip("Maps of the Acorns mode online (content pack, Server-Spec §5.4). The offline match uses DefaultMap.")]
        public List<MapConfig> AcornsMaps = new List<MapConfig>();

        [Tooltip("Rules of the Last Beast mode (10 fighters, each on their own). Empty = the mode is not in the content pack.")]
        public LastBeastModeConfig LastBeastMode;

        [Tooltip("Maps of the Last Beast mode (content pack, Server-Spec §9).")]
        public List<MapConfig> LastBeastMaps = new List<MapConfig>();

        [Tooltip("UI strings EN/RU (D-019).")]
        public LocTable Strings;

        [Tooltip("Bot skill levels offered in the menu (GDD §7).")]
        public List<BotDifficultyConfig> BotDifficulties = new List<BotDifficultyConfig>();

        [Tooltip("Bot behaviour shared by all difficulties.")]
        public BotBehaviourConfig BotBehaviour;

        [Tooltip("Online client settings: regions, content pack, prediction and interpolation (Server-Spec §12.3).")]
        public NetConfig Net;

        [Tooltip("Procedural animation of the fighter models (view only, D-109).")]
        public FighterAnimConfig FighterAnim;

        [Tooltip("Match audio: mixer, voice limit, distance fade, fighters' sound sets (brief A-TheoSfx).")]
        public AudioConfig Audio;

        /// <summary>Fighter config by id, or null.</summary>
        public FighterConfig FindFighter(string id)
        {
            for (int i = 0; i < Fighters.Count; i++)
                if (Fighters[i] != null && string.Equals(Fighters[i].Id, id, StringComparison.Ordinal)) return Fighters[i];
            return null;
        }

        /// <summary>Map config by id among the default, Acorns and Last Beast maps, or null (views find a map's decor and theme, D-140).
        /// No allocations.</summary>
        public MapConfig FindMap(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (DefaultMap != null && string.Equals(DefaultMap.Id, id, StringComparison.Ordinal)) return DefaultMap;
            MapConfig found = FindMap(AcornsMaps, id);
            return found != null ? found : FindMap(LastBeastMaps, id);
        }

        private static MapConfig FindMap(List<MapConfig> maps, string id)
        {
            if (maps == null) return null;
            for (int i = 0; i < maps.Count; i++)
                if (maps[i] != null && string.Equals(maps[i].Id, id, StringComparison.Ordinal)) return maps[i];
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
