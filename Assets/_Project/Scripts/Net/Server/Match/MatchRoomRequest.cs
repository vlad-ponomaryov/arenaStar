using System;
using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Net.Content;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// What a room is created from (the lobby fills it in S3.8; tests build it with <see cref="FromBundle"/>): defs and setup of
    /// the match, its map index and mode in the content pack (for <c>MatchStart</c>), and which slots wait for a player.
    /// </summary>
    public sealed class MatchRoomRequest
    {
        public MatchDefs Defs;
        public MatchSetup Setup;
        public int MapIndex;
        public ModeKind Mode;

        /// <summary>Per slot: a player will connect (true) or a bot plays the whole match (false).</summary>
        public bool[] PlayerSlots;

        /// <summary>
        /// Builds the line-up from the content pack: <paramref name="fighters"/> has one entry per slot — the fighter index a
        /// player chose, or −1 for a bot (a random fighter by <paramref name="seed"/>, no repeats within a team). Team modes
        /// fill both teams with the map's team spawns; Last Beast is free-for-all on the shared spawns
        /// (<see cref="MatchSetupBuilder"/>). Level and the unlock flag — from the pack's policy.
        /// </summary>
        public static MatchRoomRequest FromBundle(ContentBundle bundle, int mapIndex, ModeKind mode, int[] fighters, int seed)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            if (fighters == null || fighters.Length == 0) throw new ArgumentException("MatchRoomRequest: no slots");
            MatchDefs defs = bundle.CreateMatchDefs(mapIndex, mode);
            int slots = fighters.Length;
            var chosen = new int[slots];
            var isPlayer = new bool[slots];
            for (int i = 0; i < slots; i++)
            {
                isPlayer[i] = fighters[i] >= 0;
                chosen[i] = fighters[i];
                if (fighters[i] >= defs.Fighters.Length) throw new ArgumentException("MatchRoomRequest: fighter " + fighters[i] + " out of range");
            }
            bool ffa = mode == ModeKind.LastBeast;
            int teamSize = ffa ? 0 : defs.Map.SpawnCount(0);
            MatchSetupBuilder.PickRandomFighters(defs, chosen, isPlayer, teamSize, seed);
            ContentMatchPolicy policy = bundle.Policy;
            MatchSetup setup = ffa
                ? MatchSetupBuilder.CreateFreeForAll(defs, chosen, policy.FighterLevel, policy.UnlockAllAbilities, seed)
                : MatchSetupBuilder.CreateTeams(defs, chosen, teamSize, policy.FighterLevel, policy.UnlockAllAbilities, seed);
            return new MatchRoomRequest { Defs = defs, Setup = setup, MapIndex = mapIndex, Mode = mode, PlayerSlots = isPlayer };
        }

        public void Validate()
        {
            if (Defs == null || Setup == null) throw new ArgumentException("MatchRoomRequest: defs and setup required");
            Setup.Validate(Defs);
            if (PlayerSlots == null || PlayerSlots.Length != Setup.SlotCount)
                throw new ArgumentException("MatchRoomRequest: PlayerSlots must have one entry per slot");
        }
    }
}
