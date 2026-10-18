namespace Arena.Online
{
    /// <summary>Where the <see cref="OnlineSession"/> is (Server-Spec §12.3).</summary>
    public enum OnlinePhase : byte
    {
        /// <summary>No connection.</summary>
        Idle = 0,

        /// <summary>Looking for a LAN server (<c>Discover</c>) or resolving the region host.</summary>
        Locating = 1,

        /// <summary>Handshake with the lobby.</summary>
        Connecting = 2,

        /// <summary>In the queue (<c>QueueStatus</c> once a second).</summary>
        Searching = 3,

        /// <summary><c>MatchStart</c> came: the Match scene is loading.</summary>
        Starting = 4,

        /// <summary>The Match scene took the session (<see cref="OnlineSession.TakeMatch"/>).</summary>
        InMatch = 5,

        /// <summary>The search ended with an error (<see cref="OnlineSession.Failure"/>).</summary>
        Failed = 6,
    }
}
