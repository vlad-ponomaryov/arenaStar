using System;
using Arena.Core.Map;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Content;
using Arena.Net.Server.Match;

namespace Arena.Net.Server.Lobby
{
    /// <summary>
    /// Turns a group of queued players into a <see cref="MatchRoomRequest"/> (Server-Spec §6.2): the map (seeded pick among the
    /// mode's maps), the slots of the players and bots in the free slots (fill bots play at <c>FillBotDifficulty</c> — the host's).
    /// Team modes: bots are split evenly between the teams, so the players are too — team 0 gets ⌈n/2⌉, team 1 ⌊n/2⌋
    /// (1 → 1 + 2 bots vs 3 bots, 3 → 2 + 1, 5 → 3 + 2); among those splits the one with the fewest equal fighters in a team
    /// is taken (ties — the first in join order), equal fighters in a team stay allowed when unavoidable (D-087). Players take
    /// the first slots of their team in join order. Free-for-all (Last Beast): player k → slot k. Bots get seeded random
    /// fighters without repeats in a team (<c>MatchSetupBuilder.PickRandomFighters</c>). Allocates (once per match).
    /// </summary>
    public sealed class RoomAssembler
    {
        /// <summary>Exhaustive split search is over player bitmasks; a match has at most this many players.</summary>
        private const int MaxSplitPlayers = SimLimits.MaxFighters;

        private readonly ContentBundle _bundle;

        public RoomAssembler(ContentBundle bundle)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        }

        /// <summary>The mode is played online here (it has maps in the content pack).</summary>
        public bool IsPlayable(ModeKind mode)
        {
            ContentModeMaps maps = _bundle.MapsFor(mode);
            return mode != ModeKind.None && maps != null && maps.MapIndices.Length > 0 && Capacity(mode, out _) > 0;
        }

        /// <summary>
        /// Slots of a match of <paramref name="mode"/>: the smallest over its maps (so any map fits), at most
        /// <c>SimLimits.MaxFighters</c>. <paramref name="teamSize"/> — slots per team, 0 for free-for-all. 0 if not playable.
        /// </summary>
        public int Capacity(ModeKind mode, out int teamSize)
        {
            teamSize = 0;
            ContentModeMaps maps = _bundle.MapsFor(mode);
            if (maps == null || maps.MapIndices.Length == 0) return 0;
            bool ffa = mode == ModeKind.LastBeast;
            int best = int.MaxValue;
            for (int i = 0; i < maps.MapIndices.Length; i++)
            {
                MapDef map = _bundle.Maps[maps.MapIndices[i]];
                int n = ffa ? map.SpawnCount(MapSpawn.SharedTeam) : Math.Min(map.SpawnCount(0), map.SpawnCount(1));
                if (n < best) best = n;
            }
            if (ffa) return Math.Min(best, SimLimits.MaxFighters);
            teamSize = Math.Min(best, SimLimits.MaxFighters / 2);
            return teamSize * 2;
        }

        /// <summary>
        /// The request for <paramref name="count"/> players with fighters <paramref name="fighters"/> (join order);
        /// <paramref name="playerSlots"/> receives the slot of each player.
        /// </summary>
        public MatchRoomRequest Assemble(ModeKind mode, int[] fighters, int count, int seed, int[] playerSlots)
        {
            if (fighters == null || playerSlots == null) throw new ArgumentNullException(nameof(fighters));
            int capacity = Capacity(mode, out int teamSize);
            if (capacity <= 0) throw new ArgumentException("RoomAssembler: mode " + mode + " is not playable");
            if (count <= 0 || count > capacity || count > fighters.Length || count > playerSlots.Length)
                throw new ArgumentException("RoomAssembler: " + count + " players for " + capacity + " slots");

            ContentModeMaps maps = _bundle.MapsFor(mode);
            int mapIndex = maps.MapIndices[(int)((uint)seed % (uint)maps.MapIndices.Length)];
            var slotFighters = new int[capacity];
            for (int i = 0; i < capacity; i++) slotFighters[i] = -1;

            if (teamSize == 0)
            {
                for (int k = 0; k < count; k++)
                {
                    playerSlots[k] = k;
                    slotFighters[k] = fighters[k];
                }
            }
            else
            {
                int mask = BestSplit(fighters, count);
                int next0 = 0, next1 = teamSize;
                for (int k = 0; k < count; k++)
                {
                    int slot = (mask & (1 << k)) != 0 ? next0++ : next1++;
                    playerSlots[k] = slot;
                    slotFighters[slot] = fighters[k];
                }
            }
            return MatchRoomRequest.FromBundle(_bundle, mapIndex, mode, slotFighters, seed, teamSize);
        }

        /// <summary>
        /// Bit k set — player k goes to team 0. Team 0 gets ⌈n/2⌉ players; the split with the fewest pairs of equal fighters
        /// inside a team wins, ties — the lowest mask (earlier players first to team 0).
        /// </summary>
        public static int BestSplit(int[] fighters, int count)
        {
            if (count <= 0) return 0;
            if (count > MaxSplitPlayers) throw new ArgumentException("RoomAssembler: too many players for a split");
            int team0 = (count + 1) / 2;
            int bestMask = -1, bestCost = int.MaxValue;
            for (int mask = 0; mask < 1 << count; mask++)
            {
                if (PopCount(mask) != team0) continue;
                int cost = 0;
                for (int a = 0; a < count; a++)
                for (int b = a + 1; b < count; b++)
                {
                    bool sameTeam = ((mask >> a) & 1) == ((mask >> b) & 1);
                    if (sameTeam && fighters[a] == fighters[b]) cost++;
                }
                if (cost >= bestCost) continue;
                bestCost = cost;
                bestMask = mask;
                if (cost == 0) break;
            }
            return bestMask;
        }

        private static int PopCount(int v)
        {
            int n = 0;
            while (v != 0)
            {
                v &= v - 1;
                n++;
            }
            return n;
        }
    }
}
