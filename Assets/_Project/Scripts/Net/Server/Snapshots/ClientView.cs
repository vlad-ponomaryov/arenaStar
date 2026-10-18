using Arena.Net.Snapshots;

namespace Arena.Net.Server.Snapshots
{
    /// <summary>
    /// What the server knows about one client's views (Server-Spec §4.4 "Дельта"): the ring of sent views — each exactly what
    /// the client restores — with the transport sequence of the packet that carried it and whether it was acknowledged. The
    /// baseline of the next snapshot is the newest acknowledged view still in range; none — a key frame. A key frame request
    /// from the client (it lost its baseline) forgets every acknowledgement. No allocations after construction.
    /// </summary>
    public sealed class ClientView
    {
        private readonly int[] _sequence = new int[NetLimits.SnapshotHistory];
        private readonly bool[] _acked = new bool[NetLimits.SnapshotHistory];

        public ClientView(int slot)
        {
            Slot = slot;
            Reset();
        }

        /// <summary>The client's fighter slot.</summary>
        public int Slot { get; }

        public SnapshotFrameRing Sent { get; } = new SnapshotFrameRing();

        public long KeyframesSent { get; private set; }
        public long DeltasSent { get; private set; }

        public void Reset()
        {
            Sent.Clear();
            for (int i = 0; i < _sequence.Length; i++)
            {
                _sequence[i] = -1;
                _acked[i] = false;
            }
        }

        /// <summary>The newest acknowledged view that can be the baseline of a snapshot of <paramref name="tick"/>, or null.</summary>
        public SnapshotFrame ChooseBaseline(int tick)
        {
            SnapshotFrame best = null;
            for (int i = 0; i < _acked.Length; i++)
            {
                if (!_acked[i]) continue;
                SnapshotFrame f = Sent.At(i);
                if (!f.Valid || !SnapshotFrameRing.InBaselineRange(tick, f.Header.ServerTick)) continue;
                if (best == null || unchecked(f.Header.ServerTick - best.Header.ServerTick) > 0) best = f;
            }
            return best;
        }

        /// <summary>The ring slot for the view of <paramref name="tick"/> (its old acknowledgement is forgotten).</summary>
        public SnapshotFrame BeginFrame(int tick)
        {
            int slot = SnapshotFrameRing.SlotOf(tick);
            _sequence[slot] = -1;
            _acked[slot] = false;
            return Sent.Acquire(tick);
        }

        /// <summary>Counts a built view (statistics).</summary>
        public void CountBuilt(bool keyframe)
        {
            if (keyframe) KeyframesSent++;
            else DeltasSent++;
        }

        /// <summary>The view of <paramref name="tick"/> went out in the packet <paramref name="sequence"/>.</summary>
        public void MarkSent(int tick, ushort sequence)
        {
            if (Sent.Get(tick) == null) return;
            _sequence[SnapshotFrameRing.SlotOf(tick)] = sequence;
        }

        /// <summary>A packet was acknowledged (<c>IConnectionHandler.OnPacketAcked</c>): its view becomes a baseline candidate.</summary>
        public void OnPacketAcked(ushort sequence)
        {
            for (int i = 0; i < _sequence.Length; i++)
            {
                if (_sequence[i] != sequence || !Sent.At(i).Valid) continue;
                _acked[i] = true;
                _sequence[i] = -1;
            }
        }

        /// <summary>Marks the view of <paramref name="tick"/> acknowledged directly (tests, in-process clients).</summary>
        public void AckTick(int tick)
        {
            if (Sent.Get(tick) == null) return;
            _acked[SnapshotFrameRing.SlotOf(tick)] = true;
        }

        /// <summary>The client lost its baseline: next snapshot is a key frame.</summary>
        public void RequestKeyframe()
        {
            for (int i = 0; i < _acked.Length; i++) _acked[i] = false;
        }
    }
}
