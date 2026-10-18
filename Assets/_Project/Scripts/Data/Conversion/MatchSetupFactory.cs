using System;
using Arena.Core.Defs;

namespace Arena.Data.Conversion
{
    /// <summary>
    /// Builds the prototype <see cref="MatchSetup"/> from <see cref="MatchRunnerConfig"/>: slots [0, TeamSize) are team 0
    /// (bottom), the rest team 1 (top). The k-th fighter of a team spawns at the k-th spawn cell of that team on the map
    /// (centre of the cell). The menu's fighter choice goes to the local slot: the fighter swaps places with the slot that
    /// had it (so the line-up keeps every fighter once), or replaces the local one if it was not in the line-up.
    /// With <c>randomOthers</c> every other slot (the bots) gets a random fighter from <c>MatchDefs.Fighters</c> drawn by the
    /// match seed, without repeats within a team while there are enough fighters (S2.8).
    /// Layout and the random fighters are the pure <see cref="MatchSetupBuilder"/> (shared with the server, S3.6).
    /// Called once per match start.
    /// </summary>
    public static class MatchSetupFactory
    {
        public static MatchSetup CreateDefault(MatchRunnerConfig config, MatchDefs defs) => Create(config, defs, null, config != null ? config.Seed : 0);

        /// <param name="localFighterId">Fighter of the local slot, or null for the config's line-up.</param>
        /// <param name="seed">Seed of the match RNG.</param>
        public static MatchSetup Create(MatchRunnerConfig config, MatchDefs defs, string localFighterId, int seed) =>
            Create(config, defs, localFighterId, seed, false);

        /// <param name="randomOthers">Slots other than the local one get random fighters (seeded, no repeats within a team).</param>
        public static MatchSetup Create(MatchRunnerConfig config, MatchDefs defs, string localFighterId, int seed, bool randomOthers)
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

            var fighters = new int[count];
            for (int i = 0; i < count; i++)
            {
                fighters[i] = defs.IndexOfFighter(ids[i]);
                bool replaced = randomOthers && i != config.LocalSlot;
                if (fighters[i] < 0 && !replaced)
                    throw new ArgumentException("MatchRunnerConfig: unknown fighter id '" + ids[i] + "' in slot " + i);
            }
            if (randomOthers)
            {
                var isFixed = new bool[count];
                isFixed[config.LocalSlot] = true;
                MatchSetupBuilder.PickRandomFighters(defs, fighters, isFixed, config.TeamSize, seed);
            }
            return MatchSetupBuilder.CreateTeams(defs, fighters, config.TeamSize, config.FighterLevel, config.UnlockAllAbilities, seed);
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
