namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Ring of <see cref="NetLimits.SnapshotHistory"/> views by tick (Server-Spec §4.4): the server's sent views and the
    /// client's received ones. Slot = tick mod history (negative countdown ticks too); a slot holds the frame of one tick at a
    /// time, so a baseline must be newer than <c>tick − SnapshotHistory</c>. Allocated once.
    /// </summary>
    public sealed class SnapshotFrameRing
    {
        private const int Mask = NetLimits.SnapshotHistory - 1;

        private readonly SnapshotFrame[] _frames = new SnapshotFrame[NetLimits.SnapshotHistory];

        public SnapshotFrameRing()
        {
            for (int i = 0; i < _frames.Length; i++) _frames[i] = new SnapshotFrame();
        }

        public int Length => _frames.Length;

        /// <summary>True if <paramref name="baselineTick"/> can serve as the baseline of a snapshot of <paramref name="tick"/>.</summary>
        public static bool InBaselineRange(int tick, int baselineTick)
        {
            int age = unchecked(tick - baselineTick);
            return age > 0 && age < NetLimits.SnapshotHistory;
        }

        public static int SlotOf(int tick) => tick & Mask;

        /// <summary>The valid view of <paramref name="tick"/>, or null.</summary>
        public SnapshotFrame Get(int tick)
        {
            SnapshotFrame f = _frames[SlotOf(tick)];
            return f.Valid && f.Header.ServerTick == tick ? f : null;
        }

        /// <summary>The slot for <paramref name="tick"/>, marked invalid: the caller fills it and sets <c>Valid</c>.</summary>
        public SnapshotFrame Acquire(int tick)
        {
            SnapshotFrame f = _frames[SlotOf(tick)];
            f.Valid = false;
            f.Header.ServerTick = tick;
            return f;
        }

        /// <summary>Frame by ring position (iteration, tests).</summary>
        public SnapshotFrame At(int index) => _frames[index];

        public void Clear()
        {
            for (int i = 0; i < _frames.Length; i++) _frames[i].Valid = false;
        }
    }
}
