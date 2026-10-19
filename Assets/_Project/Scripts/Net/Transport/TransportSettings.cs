namespace Arena.Net.Transport
{
    /// <summary>
    /// Tunable transport parameters (Server-Spec §3.2, §13). The server reads them from <c>server.json</c> (S3.9), the client
    /// from <c>NetConfig</c> (S3.10), tests set them in code. <see cref="CreateDefault"/> holds the PROTO defaults of §13 —
    /// the single place in code for these numbers (D-093). Times are milliseconds.
    /// </summary>
    public sealed class TransportSettings
    {
        /// <summary>Lower bound of the reliable retransmission timeout (§13 <c>MinRtoMs</c>).</summary>
        public int MinRtoMs;

        /// <summary>Upper bound of the retransmission timeout.</summary>
        public int MaxRtoMs;

        /// <summary>Retransmission timeout before the first RTT sample.</summary>
        public int InitialRtoMs;

        /// <summary>An empty packet goes out if nothing was sent for this long (§13 <c>KeepaliveSeconds</c>).</summary>
        public int KeepaliveMs;

        /// <summary>The connection is lost after this long without packets (§13 <c>DisconnectTimeoutSeconds</c>).</summary>
        public int DisconnectTimeoutMs;

        /// <summary>The client gives up a handshake after this long (§13 <c>ConnectTimeoutSeconds</c>).</summary>
        public int ConnectTimeoutMs;

        /// <summary>The client repeats <c>ConnectRequest</c> / <c>ConnectResponse</c> this often while it has no answer.</summary>
        public int HandshakeRetryMs;

        /// <summary>A handshake cookie is valid in its time slice and the next one: lifetime is one to two slices.</summary>
        public int CookieSliceMs;

        /// <summary>Received reliable messages are acknowledged within this delay even without outgoing traffic.</summary>
        public int AckDelayMs;

        /// <summary><c>PathChallenge</c> is repeated this often until the client answers from its new address.</summary>
        public int PathChallengeRetryMs;

        /// <summary>An explicit <c>Disconnect</c> is sent this many times, unreliably (§3.2).</summary>
        public int DisconnectRepeats;

        /// <summary>
        /// Verified handshakes per IP per minute, and separately pings + discovery per IP per minute (§13; generous because of
        /// CGNAT). Unverified traffic does not count (D-117).
        /// </summary>
        public int HandshakesPerIpPerMinute;

        /// <summary>Incoming packets per second per connection; over the limit — silent drop (§13).</summary>
        public int PacketsPerSecond;

        /// <summary>Incoming bytes per second per connection (§13).</summary>
        public int BytesPerSecond;

        /// <summary>Over a per-connection limit continuously for this long → <c>Disconnect(RateLimited)</c>.</summary>
        public int RateLimitDisconnectMs;

        /// <summary>Simultaneous connections of the server (§13 <c>MaxConnections</c>).</summary>
        public int MaxConnections;

        /// <summary>The server answers LAN <c>Discover</c> broadcasts from private addresses (§13 <c>LanDiscovery</c>).</summary>
        public bool LanDiscovery;

        /// <summary>Localization key index of the server name in <c>DiscoverReply</c>.</summary>
        public ushort ServerNameKey;

        /// <summary>PROTO defaults of Server-Spec §13 (D-093).</summary>
        public static TransportSettings CreateDefault() => new TransportSettings
        {
            MinRtoMs = 100,
            MaxRtoMs = 2000,
            InitialRtoMs = 500,
            KeepaliveMs = 1000,
            DisconnectTimeoutMs = 5000,
            ConnectTimeoutMs = 5000,
            HandshakeRetryMs = 250,
            CookieSliceMs = 10000,
            AckDelayMs = 20,
            PathChallengeRetryMs = 250,
            DisconnectRepeats = 3,
            HandshakesPerIpPerMinute = 120,
            PacketsPerSecond = 120,
            BytesPerSecond = 16384,
            RateLimitDisconnectMs = 5000,
            MaxConnections = 2000,
            LanDiscovery = true,
            ServerNameKey = 0,
        };

        /// <summary>
        /// Throws if a value would break the transport (F2: a zero cookie slice divides by zero, zero retries spin) — a bad
        /// <c>server.json</c> fails at start, not in the game thread.
        /// </summary>
        public void Validate()
        {
            if (MinRtoMs <= 0 || MaxRtoMs < MinRtoMs || InitialRtoMs <= 0) throw new System.ArgumentException("TransportSettings: RTO bounds");
            if (KeepaliveMs <= 0 || DisconnectTimeoutMs <= 0 || ConnectTimeoutMs <= 0)
                throw new System.ArgumentException("TransportSettings: keepalive and timeouts must be > 0");
            if (HandshakeRetryMs <= 0 || PathChallengeRetryMs <= 0 || CookieSliceMs <= 0)
                throw new System.ArgumentException("TransportSettings: retry intervals and CookieSliceMs must be > 0");
            if (AckDelayMs < 0 || DisconnectRepeats < 0 || RateLimitDisconnectMs < 0)
                throw new System.ArgumentException("TransportSettings: AckDelayMs, DisconnectRepeats, RateLimitDisconnectMs must be ≥ 0");
            if (HandshakesPerIpPerMinute <= 0 || PacketsPerSecond <= 0 || BytesPerSecond <= 0 || MaxConnections <= 0)
                throw new System.ArgumentException("TransportSettings: limits must be > 0");
        }

        public TransportSettings Clone() => (TransportSettings)MemberwiseClone();
    }
}
