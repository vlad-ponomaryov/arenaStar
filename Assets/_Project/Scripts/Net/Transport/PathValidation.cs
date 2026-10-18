namespace Arena.Net.Transport
{
    /// <summary>
    /// Address change of a live connection (NAT rebinding, Wi-Fi ↔ LTE; §3.2 "Смена адреса"). A packet with a known
    /// connection id from a new address starts a challenge to that address; the server switches the connection's address only
    /// when the client answers <c>PathResponse</c> with the same data from it. The challenge is never larger than the packet
    /// that triggered it (no amplification) and is repeated at most every <c>PathChallengeRetryMs</c>.
    /// </summary>
    public struct PathValidation
    {
        public bool Pending;
        public NetAddress Candidate;
        public ulong Challenge;
        public long LastSentMicros;

        public void Reset()
        {
            Pending = false;
            Candidate = default;
            Challenge = 0ul;
            LastSentMicros = 0L;
        }

        public void Start(in NetAddress candidate, ulong challenge)
        {
            Pending = true;
            Candidate = candidate;
            Challenge = challenge;
            LastSentMicros = long.MinValue;
        }

        public bool IsDue(long nowMicros, long retryMicros) =>
            Pending && (LastSentMicros == long.MinValue || nowMicros - LastSentMicros >= retryMicros);
    }
}
