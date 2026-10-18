namespace Arena.Net.Transport
{
    /// <summary>Counters of <see cref="ServerTransport"/> for metrics and tests (§5.6). Plain fields, no allocations.</summary>
    public sealed class ServerTransportCounters
    {
        public long PacketsReceived;
        public long BytesReceived;
        public long PacketsSent;
        public long BytesSent;
        public long SendFailures;
        public long DroppedMalformed;
        public long DroppedOversize;
        public long DroppedUnknownConnection;
        public long DroppedRateLimited;
        public long Challenges;
        public long Accepted;
        public long AcceptRepeats;
        public long Rejected;
        public long BadCookies;
        public long Pongs;
        public long DiscoverReplies;
        public long PathChallenges;
        public long PathChanges;
        public long Timeouts;
        public long RateLimitDisconnects;
    }
}
