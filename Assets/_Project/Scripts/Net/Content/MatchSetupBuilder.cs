using System;
using Arena.Core.Defs;
using Arena.Core.Map;

namespace Arena.Net.Content
{
    /// <summary>
    /// <see cref="MatchSetup"/> of a match built from a content pack, without Unity (server, headless tests). Same layout as
    /// the offline <c>MatchSetupFactory</c>: slots [0, teamSize) are team 0, the rest team 1; the k-th fighter of a team spawns
    /// at the k-th spawn cell of its team (cell centre); level and the unlock flag come from <see cref="ContentMatchPolicy"/>.
    /// Called once per match start.
    /// </summary>
    public static class MatchSetupBuilder
    {
        /// <param name="fighterIndices">Fighter index per slot (into <see cref="MatchDefs.Fighters"/>).</param>
        public static MatchSetup Create(MatchDefs defs, ContentMatchPolicy policy, int[] fighterIndices, int teamSize, int seed)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (fighterIndices == null || fighterIndices.Length == 0) throw new ArgumentException("MatchSetupBuilder: no slots");
            if (teamSize <= 0) throw new ArgumentException("MatchSetupBuilder: team size must be > 0");

            var setup = new MatchSetup
            {
                Seed = seed, Slots = new PlayerSlotSetup[fighterIndices.Length], UnlockAllAbilities = policy.UnlockAllAbilities
            };
            for (int i = 0; i < fighterIndices.Length; i++)
            {
                int team = i < teamSize ? 0 : 1;
                int indexInTeam = team == 0 ? i : i - teamSize;
                if (!defs.Map.TryGetSpawn(team, indexInTeam, out GridCell spawn))
                    throw new ArgumentException("Map '" + defs.Map.Id + "' has no spawn " + indexInTeam + " for team " + team + " (slot " + i + ")");
                setup.Slots[i] = new PlayerSlotSetup(fighterIndices[i], team, policy.FighterLevel, spawn.Center);
            }
            setup.Validate(defs);
            return setup;
        }
    }
}
