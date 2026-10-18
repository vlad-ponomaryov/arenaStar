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

        /// <summary>Inputs simulated with a rewind (lag compensation, S3.13).</summary>
        public long RewoundInputs;

        /// <summary>Suspicious: inputs whose <c>ViewDelayTicks</c> was above the server's limit and was cut (§8).</summary>
        public long ViewDelayClamped;

        public long Takeovers;
        public long Leaves;

        /// <summary>A player came back to his slot after being connected before (rejoin, S3.12).</summary>
        public long Rejoins;
        public long KeyframeRequests;

        // Client statistics (ClientStats, §5.6, S3.15): sums of the clients' own prediction and presentation counters.
        public long ClientStatsReports;
        public long ClientPredictionSamples;
        public long ClientPredictionCorrections;
        public long ClientLargeCorrections;
        public long ClientMaxErrorMilliCells;
        public long ClientPresentedTicks;
        public long ClientExtrapolatedTicks;
        public long ClientTeleports;
        public long ClientLargeTeleports;

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
