namespace Arena.Net.Server.Match
{
    /// <summary>
    /// Counters of a <see cref="MatchHost"/> (Server-Spec §5.6): the game thread increments them, the metrics endpoint (S3.9)
    /// publishes a copy once a second. Plain fields, no allocations; time is measured by the host process, not here.
    /// </summary>
    public sealed class ServerMetrics
    {
        public long MatchesCreated;
        public long MatchesEnded;
        public long MatchesClosed;
        public int ActiveMatches;
        public int ConnectedPlayers;

        public long Ticks;
        public long SkippedTicks;

        public long SnapshotsSent;
        public long SnapshotBytes;
        public int MaxSnapshotBytes;
        public long Keyframes;
        public long SnapshotsNotFitting;

        public long InputPackets;
        public long InputsStored;
        public long LateInputs;
        public long TooFarAheadInputs;
        public long MissingInputTicks;
        public long AppliedInputTicks;
        public long PressesCarried;
        public long PressesDropped;

        public long Takeovers;
        public long Leaves;
        public long KeyframeRequests;

        public long RejectedConnections;
        public long MalformedMessages;
        public long ReliableSendFailures;

        public double AverageSnapshotBytes => SnapshotsSent == 0 ? 0.0 : (double)SnapshotBytes / SnapshotsSent;

        /// <summary>Share of simulated player ticks without a real input in time (late or lost, §11.1: &lt; 1 % in CI conditions).</summary>
        public double MissingInputShare
        {
            get
            {
                long total = MissingInputTicks + AppliedInputTicks;
                return total == 0 ? 0.0 : (double)MissingInputTicks / total;
            }
        }
    }
}
