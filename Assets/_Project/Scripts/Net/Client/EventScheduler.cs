using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;

namespace Arena.Net.Client
{
    /// <summary>
    /// Server view events on the display timeline (Server-Spec §2.6–2.7): the copies repeated by later snapshots
    /// (<c>EventRedundancyTicks</c>) are dropped; an event is released to the presentation when the render clock reaches its
    /// tick (the damage number appears when the projectile on screen arrives), except events whose source is the own predicted
    /// fighter — they are released at once (the own hit is felt ≈ 100 ms earlier), with the number at the shown position of the
    /// target. The server's <c>AbilityFired</c> of the own predicted fighter is not replayed (the prediction showed it).
    /// An event of server tick t is in the frame of tick t + 1 (the state after t): it is due when the render tick reaches t + 1.
    /// Released events carry presentation ticks: the event tick + the shift, or the prediction tick for immediate ones.
    /// No allocations.
    /// </summary>
    public sealed class EventScheduler
    {
        private readonly NetSnapshotEvent[] _pending = new NetSnapshotEvent[NetLimits.MaxScheduledEvents];
        private readonly bool[] _immediate = new bool[NetLimits.MaxScheduledEvents];
        private readonly NetSnapshotEvent[] _recent = new NetSnapshotEvent[NetLimits.RecentEventHistory];
        private int _count;
        private int _recentNext;
        private int _recentCount;

        public int PendingCount => _count;
        public long Received { get; private set; }
        public long Duplicates { get; private set; }
        public long Released { get; private set; }
        public long ReleasedImmediately { get; private set; }
        public long Dropped { get; private set; }

        /// <summary>Events of a decoded snapshot (also a late one); <paramref name="ownPredicted"/> — the own fighter is predicted.</summary>
        public void Add(NetSnapshotEvent[] events, int count, int ownSlot, bool ownPredicted)
        {
            for (int i = 0; i < count; i++)
            {
                ref NetSnapshotEvent e = ref events[i];
                // Equal events in one tick are possible (pellets of one shot): a snapshot carries all events of its window, so
                // the k-th equal copy in this snapshot is new only if fewer than k equal ones were taken before.
                int copy = 1;
                for (int j = 0; j < i; j++)
                    if (events[j].Equals(e)) copy++;
                if (CountRecent(in e) >= copy)
                {
                    Duplicates++;
                    continue;
                }
                Remember(in e);
                Received++;
                bool own = ownPredicted && e.Source == ownSlot && ownSlot >= 0;
                if (own && e.Type == SimEventType.AbilityFired) continue;
                if (_count >= _pending.Length)
                {
                    Dropped++;
                    continue;
                }
                _pending[_count] = e;
                _immediate[_count] = own;
                _count++;
            }
        }

        /// <summary>
        /// Moves the events due at <paramref name="renderTick"/> into <paramref name="output"/> (ticks shifted by
        /// <paramref name="shift"/>); <paramref name="shown"/> gives target positions for events without a point.
        /// </summary>
        public void Release(int renderTick, int shift, int predictionTick, MatchState shown, int ownTeam, SimEventBuffer output)
        {
            int kept = 0;
            for (int i = 0; i < _count; i++)
            {
                ref NetSnapshotEvent e = ref _pending[i];
                bool immediate = _immediate[i];
                if (!immediate && e.Tick >= renderTick)
                {
                    if (kept != i)
                    {
                        _pending[kept] = e;
                        _immediate[kept] = false;
                    }
                    kept++;
                    continue;
                }
                Vec2 position = Vec2.Zero;
                bool shownTarget = (uint)e.Target < (uint)shown.Fighters.Length && shown.Fighters[e.Target].Active
                    && shown.Fighters[e.Target].IsVisibleTo(ownTeam);
                if (immediate && shownTarget) position = shown.Fighters[e.Target].Position;
                else if (e.HasPoint) position = new Vec2(Quantize.FromPosition(e.PointX), Quantize.FromPosition(e.PointY));
                else if (shownTarget) position = shown.Fighters[e.Target].Position;
                int tick = immediate ? predictionTick : e.Tick + shift;
                output.Add(new SimEvent(e.Type, tick, e.Source, e.Target, e.Value, position, Vec2.Zero, e.AbilityRef));
                Released++;
                if (immediate) ReleasedImmediately++;
            }
            _count = kept;
        }

        public void Clear()
        {
            _count = 0;
            _recentCount = 0;
            _recentNext = 0;
        }

        private int CountRecent(in NetSnapshotEvent e)
        {
            int n = 0;
            for (int i = 0; i < _recentCount; i++)
                if (_recent[i].Equals(e)) n++;
            return n;
        }

        private void Remember(in NetSnapshotEvent e)
        {
            _recent[_recentNext] = e;
            _recentNext = (_recentNext + 1) % _recent.Length;
            if (_recentCount < _recent.Length) _recentCount++;
        }
    }
}
