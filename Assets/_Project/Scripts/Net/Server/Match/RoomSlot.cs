using Arena.Net.Protocol;
using Arena.Net.Server.Snapshots;

namespace Arena.Net.Server.Match
{
    /// <summary>One slot of a <see cref="MatchRoom"/>: its player (token, connection), the input buffer, the snapshot views, pending reliable messages.</summary>
    public sealed class RoomSlot
    {
        public RoomSlot(int slot, bool isPlayer, NetToken token, ServerSettings settings)
        {
            Slot = slot;
            IsPlayer = isPlayer;
            Token = token;
            View = new ClientView(slot);
            Input = new SlotInputBuffer(settings.MaxInputAheadTicks, settings.MissingInputRepeatTicks);
            InterpDelayTicks = 0;
        }

        public int Slot { get; }

        /// <summary>A player connects to this slot (else a bot plays it the whole match).</summary>
        public bool IsPlayer { get; }

        /// <summary>The ticket the player connects with (and returns with, §7).</summary>
        public NetToken Token { get; }

        /// <summary>Transport connection index, −1 — not connected.</summary>
        public int Connection = -1;

        /// <summary><c>ClientReady</c> came over the current connection.</summary>
        public bool Ready;

        /// <summary>A connection was bound to this slot before (the next bind is a return, S3.12).</summary>
        public bool EverConnected;

        public bool MatchStartPending;
        public bool EliminatedPending;
        public bool MatchEndedPending;

        /// <summary><c>Eliminated</c> was queued once (Last Beast).</summary>
        public bool EliminatedQueued;

        public ClientView View { get; }

        public SlotInputBuffer Input { get; }

        /// <summary>The interpolation delay the client reported in its newest <c>Input</c>, ticks (lag compensation bound, §2.8).</summary>
        public int InterpDelayTicks;

        // The newest ClientStats of this slot (cumulative counters, §5.6): the next report adds only its growth to the metrics.
        public long ReportedPredictionSamples;
        public long ReportedPredictionCorrections;
        public long ReportedLargeCorrections;
        public long ReportedPresentedTicks;
        public long ReportedExtrapolatedTicks;
        public long ReportedTeleports;
        public long ReportedLargeTeleports;

        public bool IsConnected => Connection >= 0;
    }
}
