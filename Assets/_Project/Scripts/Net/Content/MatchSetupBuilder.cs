using System;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Random;

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

        /// <summary>
        /// Free-for-all setup (Last Beast, Server-Spec §9.1): slot i is team i and spawns at a shared spawn of the map
        /// (<see cref="MapSpawn.SharedTeam"/>) picked by a permutation of the shared spawns drawn from <paramref name="seed"/>
        /// (Fisher–Yates on the deterministic RNG — the same on the server and the client).
        /// </summary>
        public static MatchSetup CreateFreeForAll(MatchDefs defs, ContentMatchPolicy policy, int[] fighterIndices, int seed)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (fighterIndices == null || fighterIndices.Length == 0) throw new ArgumentException("MatchSetupBuilder: no slots");
            int spawns = defs.Map.SpawnCount(MapSpawn.SharedTeam);
            if (fighterIndices.Length > spawns)
                throw new ArgumentException("Map '" + defs.Map.Id + "' has " + spawns + " shared spawns for " + fighterIndices.Length + " slots");

            var order = new int[spawns];
            for (int i = 0; i < spawns; i++) order[i] = i;
            var rng = DeterministicRng.FromSeed(seed);
            for (int i = spawns - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                int t = order[i];
                order[i] = order[j];
                order[j] = t;
            }

            var setup = new MatchSetup
            {
                Seed = seed, Slots = new PlayerSlotSetup[fighterIndices.Length], UnlockAllAbilities = policy.UnlockAllAbilities
            };
            for (int i = 0; i < fighterIndices.Length; i++)
            {
                defs.Map.TryGetSpawn(MapSpawn.SharedTeam, order[i], out GridCell spawn);
                setup.Slots[i] = new PlayerSlotSetup(fighterIndices[i], i, policy.FighterLevel, spawn.Center);
            }
            setup.Validate(defs);
            return setup;
        }
    }
}
