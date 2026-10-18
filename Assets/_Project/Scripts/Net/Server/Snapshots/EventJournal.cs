using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;

namespace Arena.Net.Server.Snapshots
{
    /// <summary>
    /// The view events of the last <see cref="NetLimits.EventJournalTicks"/> ticks of a match (Server-Spec §4.4 item 9), filled
    /// once per server tick; every snapshot takes the events of its window with the per-client filter:
    /// <list type="bullet">
    /// <item><c>Damaged</c> / <c>Healed</c> — with point and target to a team that sees the target; if the target is hidden from
    /// the team, only the source's client gets it, without point and target slot (sound and vibration, as offline);</item>
    /// <item><c>AbilityFired</c> of an ult — to teams that see the source;</item>
    /// <item><c>UltReady</c> — to the fighter's own client only.</item>
    /// </list>
    /// Visibility is taken at the event's tick (masks after the tick, the same ones the fighter filter of that tick uses).
    /// Preallocated; overflowing events of a tick are dropped and counted.
    /// </summary>
    public sealed class EventJournal
    {
        private readonly JournalEvent[][] _buckets = new JournalEvent[NetLimits.EventJournalTicks][];
        private readonly int[] _bucketTick = new int[NetLimits.EventJournalTicks];
        private readonly int[] _bucketCount = new int[NetLimits.EventJournalTicks];
        private readonly bool[] _bucketUsed = new bool[NetLimits.EventJournalTicks];

        public EventJournal()
        {
            for (int i = 0; i < _buckets.Length; i++) _buckets[i] = new JournalEvent[NetLimits.MaxJournalEventsPerTick];
        }

        /// <summary>Events dropped because a tick had more than <see cref="NetLimits.MaxJournalEventsPerTick"/>.</summary>
        public long Dropped { get; private set; }

        public void Reset()
        {
            for (int i = 0; i < _bucketUsed.Length; i++)
            {
                _bucketUsed[i] = false;
                _bucketCount[i] = 0;
            }
            Dropped = 0;
        }

        /// <summary>Journals the view events of the tick just simulated (<c>state.Globals.Tick</c> is the snapshot tick).</summary>
        public void Capture(MatchState state, SimEventBuffer events)
        {
            int tick = state.Globals.Tick;
            int b = BucketOf(tick);
            _bucketUsed[b] = true;
            _bucketTick[b] = tick;
            _bucketCount[b] = 0;
            JournalEvent[] bucket = _buckets[b];
            for (int i = 0; i < events.Count; i++)
            {
                ref readonly SimEvent e = ref events[i];
                if (!EventPacker.IsViewEvent(e.Type)) continue;
                if (e.Type == SimEventType.AbilityFired && MatchDefs.SlotOf(e.Value) != AbilitySlot.Ult) continue;
                if (_bucketCount[b] >= bucket.Length)
                {
                    Dropped++;
                    continue;
                }
                ref JournalEvent j = ref bucket[_bucketCount[b]++];
                j.Event.Type = e.Type;
                j.Event.Tick = tick;
                j.Event.Source = e.Source;
                j.Event.Target = e.Target;
                j.Event.Value = e.Value;
                j.Event.HasPoint = true;
                j.Event.PointX = Quantize.ToPosition(e.Position.X);
                j.Event.PointY = Quantize.ToPosition(e.Position.Y);
                j.Event.AbilityRef = e.AbilityRef;
                See(state, e.Source, out j.SourceTeam, out j.SourceSeenBy);
                See(state, e.Target, out j.TargetTeam, out j.TargetSeenBy);
            }
        }

        /// <summary>
        /// Events of the ticks <c>(snapshotTick − windowTicks, snapshotTick]</c> for the client of <paramref name="slot"/> in
        /// <paramref name="team"/>, newest tick first, at most <paramref name="max"/>. Returns the count written.
        /// </summary>
        public int Collect(int snapshotTick, int windowTicks, int slot, int team, NetSnapshotEvent[] output, int max)
        {
            if (max > output.Length) max = output.Length;
            int n = 0;
            uint teamBit = (uint)team < 32u ? 1u << team : 0u;
            for (int age = 0; age < windowTicks && n < max; age++)
            {
                int tick = snapshotTick - age;
                int b = BucketOf(tick);
                if (!_bucketUsed[b] || _bucketTick[b] != tick) continue;
                JournalEvent[] bucket = _buckets[b];
                for (int i = _bucketCount[b] - 1; i >= 0 && n < max; i--)
                {
                    ref JournalEvent j = ref bucket[i];
                    switch (j.Event.Type)
                    {
                        case SimEventType.Damaged:
                        case SimEventType.Healed:
                            if (j.TargetTeam == team || (j.TargetSeenBy & teamBit) != 0u) output[n++] = j.Event;
                            else if (j.Event.Source == slot && slot >= 0)
                            {
                                NetSnapshotEvent hidden = j.Event;
                                hidden.Target = -1;
                                hidden.HasPoint = false;
                                hidden.PointX = 0;
                                hidden.PointY = 0;
                                output[n++] = hidden;
                            }
                            break;
                        case SimEventType.AbilityFired:
                            if (j.SourceTeam == team || (j.SourceSeenBy & teamBit) != 0u) output[n++] = j.Event;
                            break;
                        case SimEventType.UltReady:
                            if (j.Event.Source == slot && slot >= 0)
                            {
                                NetSnapshotEvent own = j.Event;
                                own.HasPoint = false;
                                own.PointX = 0;
                                own.PointY = 0;
                                output[n++] = own;
                            }
                            break;
                    }
                }
            }
            return n;
        }

        private static void See(MatchState state, int slot, out int team, out uint seenBy)
        {
            if ((uint)slot >= (uint)state.Globals.FighterCount)
            {
                team = -1;
                seenBy = 0u;
                return;
            }
            ref FighterState f = ref state.Fighters[slot];
            team = f.Team;
            seenBy = f.VisibleToTeams | ((uint)f.Team < 32u ? 1u << f.Team : 0u);
        }

        private static int BucketOf(int tick) => tick & (NetLimits.EventJournalTicks - 1);
    }
}
