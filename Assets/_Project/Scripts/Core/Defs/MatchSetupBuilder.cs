using System;
using Arena.Core.Map;
using Arena.Core.Random;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Pure line-up building (Server-Spec S3.6): slots, teams, spawn points and the seeded random fighters of bots — the same
    /// code offline (<c>MatchSetupFactory</c>), on the server (<c>MatchRoom</c>) and in headless tests. Team matches: slots
    /// [0, teamSize) are team 0, the rest team 1; the k-th fighter of a team spawns at the k-th spawn cell of its team (cell
    /// centre). Free-for-all (Last Beast, §9.1): slot i is team i and spawns at a shared spawn picked by a seeded permutation.
    /// Called once per match start (allocates).
    /// </summary>
    public static class MatchSetupBuilder
    {
        /// <param name="fighterIndices">Fighter index per slot (into <see cref="MatchDefs.Fighters"/>).</param>
        public static MatchSetup CreateTeams(MatchDefs defs, int[] fighterIndices, int teamSize, int level, bool unlockAllAbilities, int seed)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (fighterIndices == null || fighterIndices.Length == 0) throw new ArgumentException("MatchSetupBuilder: no slots");
            if (teamSize <= 0) throw new ArgumentException("MatchSetupBuilder: team size must be > 0");

            var setup = new MatchSetup { Seed = seed, Slots = new PlayerSlotSetup[fighterIndices.Length], UnlockAllAbilities = unlockAllAbilities };
            for (int i = 0; i < fighterIndices.Length; i++)
            {
                int team = TeamOf(i, teamSize);
                int indexInTeam = team == 0 ? i : i - teamSize;
                if (!defs.Map.TryGetSpawn(team, indexInTeam, out GridCell spawn))
                    throw new ArgumentException("Map '" + defs.Map.Id + "' has no spawn " + indexInTeam + " for team " + team + " (slot " + i + ")");
                setup.Slots[i] = new PlayerSlotSetup(fighterIndices[i], team, level, spawn.Center);
            }
            setup.Validate(defs);
            return setup;
        }

        /// <summary>
        /// Free-for-all setup: slot i is team i and spawns at a shared spawn of the map (<see cref="MapSpawn.SharedTeam"/>)
        /// picked by a permutation of the shared spawns drawn from <paramref name="seed"/> (Fisher–Yates on the deterministic
        /// RNG — the same on the server and the client).
        /// </summary>
        public static MatchSetup CreateFreeForAll(MatchDefs defs, int[] fighterIndices, int level, bool unlockAllAbilities, int seed)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
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

            var setup = new MatchSetup { Seed = seed, Slots = new PlayerSlotSetup[fighterIndices.Length], UnlockAllAbilities = unlockAllAbilities };
            for (int i = 0; i < fighterIndices.Length; i++)
            {
                defs.Map.TryGetSpawn(MapSpawn.SharedTeam, order[i], out GridCell spawn);
                setup.Slots[i] = new PlayerSlotSetup(fighterIndices[i], i, level, spawn.Center);
            }
            setup.Validate(defs);
            return setup;
        }

        /// <summary>
        /// Gives every slot that is not <paramref name="isFixed"/> a random fighter drawn from <paramref name="seed"/>, without
        /// repeats within a team while there are enough fighters (fixed slots — players who chose theirs — count as used).
        /// Teams as in <see cref="CreateTeams"/>; <paramref name="teamSize"/> ≤ 0 — free-for-all (a team per slot).
        /// Teams are filled in order (team 0 first), slots in order — the offline line-up of S2.8 is unchanged.
        /// </summary>
        public static void PickRandomFighters(MatchDefs defs, int[] fighterIndices, bool[] isFixed, int teamSize, int seed)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (fighterIndices == null) throw new ArgumentNullException(nameof(fighterIndices));
            if (isFixed == null || isFixed.Length < fighterIndices.Length) throw new ArgumentException("MatchSetupBuilder: isFixed too short");
            int fighterCount = defs.Fighters.Length;
            if (fighterCount == 0) throw new ArgumentException("MatchSetupBuilder: no fighters");
            var rng = DeterministicRng.FromSeed(seed);
            var candidates = new int[fighterCount];
            var used = new bool[fighterCount];
            int slots = fighterIndices.Length;
            int teams = teamSize > 0 ? 2 : slots;
            for (int team = 0; team < teams; team++)
            {
                int from, to;
                if (teamSize > 0)
                {
                    from = team == 0 ? 0 : teamSize;
                    to = team == 0 ? (teamSize < slots ? teamSize : slots) : slots;
                }
                else
                {
                    from = team;
                    to = team + 1;
                }
                Array.Clear(used, 0, used.Length);
                for (int i = from; i < to; i++)
                    if (isFixed[i] && (uint)fighterIndices[i] < (uint)fighterCount) used[fighterIndices[i]] = true;
                for (int i = from; i < to; i++)
                {
                    if (isFixed[i]) continue;
                    int n = 0;
                    for (int k = 0; k < fighterCount; k++)
                        if (!used[k]) candidates[n++] = k;
                    if (n == 0)
                    {
                        // More slots than fighters: repeats are unavoidable.
                        for (int k = 0; k < fighterCount; k++) candidates[k] = k;
                        n = fighterCount;
                    }
                    int pick = candidates[rng.Range(0, n)];
                    used[pick] = true;
                    fighterIndices[i] = pick;
                }
            }
        }

        /// <summary>Team of a slot in a team match.</summary>
        public static int TeamOf(int slot, int teamSize) => slot < teamSize ? 0 : 1;
    }
}
