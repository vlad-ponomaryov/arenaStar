namespace Arena.LoadTest
{
    /// <summary>Sums of the light clients' counters at one moment (the load test takes the difference over its window).</summary>
    public struct LoadTotals
    {
        public long PacketsSent;
        public long PacketsReceived;
        public long Malformed;
        public long MissingBaselines;
    }
}
