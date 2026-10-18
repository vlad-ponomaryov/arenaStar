namespace Arena.Net.Client
{
    /// <summary>Where a <see cref="LobbyClient"/> is (Server-Spec §6.1, §12.4 — the search screen).</summary>
    public enum LobbyState : byte
    {
        /// <summary>Not searching (before <c>Join</c> or after <c>Cancel</c>).</summary>
        Idle = 0,

        /// <summary>The handshake runs; <c>QueueJoin</c> goes out when connected.</summary>
        Connecting = 1,

        /// <summary><c>QueueJoin</c> sent; <c>QueueStatus</c> arrives once a second.</summary>
        Searching = 2,

        /// <summary><c>MatchAssigned</c> came: the connection belongs to the match now, <c>MatchStart</c> follows.</summary>
        Assigned = 3,

        /// <summary><c>QueueRejected</c> (see <see cref="LobbyClient.QueueRejectReason"/>), still connected.</summary>
        Rejected = 4,

        /// <summary>The connection failed or ended (see <see cref="LobbyClient.ConnectRejectReason"/> and the transport's reason).</summary>
        Failed = 5,
    }
}
