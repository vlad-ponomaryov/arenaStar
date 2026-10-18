namespace Arena.Net.Transport
{
    /// <summary>
    /// Conditions of the outgoing link of one <see cref="InMemoryNetwork"/> endpoint (Server-Spec §3.2, §11.1): one-way
    /// latency, uniform jitter, random and burst loss (Gilbert–Elliott: good/bad states), duplicates, reordering (an extra
    /// delay for some packets) and a bandwidth limit. A new instance is a perfect link. Test parameters, set by the tests.
    /// </summary>
    public sealed class NetworkConditions
    {
        public float LatencyMs;
        public float JitterMs;

        /// <summary>Loss probability in the good state, percent.</summary>
        public float LossPercent;

        /// <summary>Probability per packet to enter the bad (burst) state, percent; 0 disables bursts.</summary>
        public float BurstEnterPercent;

        /// <summary>Probability per packet to leave the bad state, percent.</summary>
        public float BurstExitPercent;

        /// <summary>Loss probability in the bad state, percent.</summary>
        public float BurstLossPercent;

        public float DuplicatePercent;

        /// <summary>Share of packets delayed by <see cref="ReorderExtraMs"/> on top (overtaken by later ones), percent.</summary>
        public float ReorderPercent;

        public float ReorderExtraMs;

        /// <summary>0 — unlimited. Packets queue behind each other; a queue longer than <see cref="MaxQueueMs"/> drops.</summary>
        public int BandwidthBytesPerSecond;

        public float MaxQueueMs;

        public NetworkConditions Clone() => (NetworkConditions)MemberwiseClone();
    }
}
