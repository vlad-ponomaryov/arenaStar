namespace Arena.Online
{
    /// <summary>Why an online search failed (Server-Spec §12.4 error texts).</summary>
    public enum OnlineFailure : byte
    {
        None = 0,

        /// <summary>No LAN server answered, the host did not resolve, the handshake timed out or the connection dropped.</summary>
        ServerUnreachable = 1,

        /// <summary>Another protocol or content version (§4.1): "update the game".</summary>
        UpdateRequired = 2,

        /// <summary>The server is in maintenance.</summary>
        Maintenance = 3,

        /// <summary>The server refused the queue (bad mode or fighter) or the session (full, rate limit, broken content).</summary>
        Rejected = 4,

        /// <summary>A remote region never answered the handshake: the network likely drops UDP (S3.11, §12.4).</summary>
        UdpBlocked = 5,

        /// <summary>"Back to the match": the server says the match is already over (S3.12).</summary>
        MatchOver = 6,
    }
}
