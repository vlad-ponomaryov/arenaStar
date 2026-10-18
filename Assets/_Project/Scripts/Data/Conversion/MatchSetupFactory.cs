using System;
using Arena.Core.Defs;
using Arena.Core.Map;

namespace Arena.Data.Conversion
{
    /// <summary>
    /// Builds the prototype <see cref="MatchSetup"/> from <see cref="MatchRunnerConfig"/>: slots [0, TeamSize) are team 0
    /// (bottom), the rest team 1 (top). The k-th fighter of a team spawns at the k-th spawn cell of that team on the map
    /// (centre of the cell). The menu's fighter choice goes to the local slot: the fighter swaps places with the slot that
    /// had it (so the line-up keeps every fighter once), or replaces the local one if it was not in the line-up.
    /// Called once per match start.
    /// </summary>
    public static class MatchSetupFactory
    {
        public static MatchSetup CreateDefault(MatchRunnerConfig config, MatchDefs defs) => Create(config, defs, null, config != null ? config.Seed : 0);

        /// <param name="localFighterId">Fighter of the local slot, or null for the config's line-up.</param>
        /// <param name="seed">Seed of the match RNG.</param>
        public static MatchSetup Create(MatchRunnerConfig config, MatchDefs defs, string localFighterId, int seed)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (config.SlotFighterIds == null || config.SlotFighterIds.Count == 0)
                throw new ArgumentException("MatchRunnerConfig.SlotFighterIds is empty");
            if (config.TeamSize <= 0) throw new ArgumentException("MatchRunnerConfig.TeamSize must be > 0");

            int count = config.SlotFighterIds.Count;
            if (config.LocalSlot < 0 || config.LocalSlot >= count)
                throw new ArgumentException("MatchRunnerConfig.LocalSlot " + config.LocalSlot + " out of range");

            string[] ids = config.SlotFighterIds.ToArray();
            if (!string.IsNullOrEmpty(localFighterId)) PutLocalFighter(ids, config.LocalSlot, localFighterId);

            var setup = new MatchSetup
            {
                Seed = seed, Slots = new PlayerSlotSetup[count], UnlockAllAbilities = config.UnlockAllAbilities
            };
            for (int i = 0; i < count; i++)
            {
                string id = ids[i];
                int fighterIndex = defs.IndexOfFighter(id);
                if (fighterIndex < 0) throw new ArgumentException("MatchRunnerConfig: unknown fighter id '" + id + "' in slot " + i);
                int team = i < config.TeamSize ? 0 : 1;
                int indexInTeam = team == 0 ? i : i - config.TeamSize;
                if (!defs.Map.TryGetSpawn(team, indexInTeam, out GridCell spawn))
                    throw new ArgumentException("Map '" + defs.Map.Id + "' has no spawn " + indexInTeam + " for team " + team + " (slot " + i + ")");
                setup.Slots[i] = new PlayerSlotSetup(fighterIndex, team, config.FighterLevel, spawn.Center);
            }
            setup.Validate(defs);
            return setup;
        }

        private static void PutLocalFighter(string[] ids, int localSlot, string fighterId)
        {
            if (string.Equals(ids[localSlot], fighterId, StringComparison.Ordinal)) return;
            for (int i = 0; i < ids.Length; i++)
            {
                if (i == localSlot || !string.Equals(ids[i], fighterId, StringComparison.Ordinal)) continue;
                ids[i] = ids[localSlot];
                break;
            }
            ids[localSlot] = fighterId;
        }
    }
}
