namespace Arena.Net.Server.Lobby
{
    /// <summary>Counters of the lobby of a <see cref="ServerNode"/> (Server-Spec §5.6). Plain fields, no allocations.</summary>
    public sealed class LobbyMetrics
    {
        /// <summary>Open connections that came for the lobby (queued, idle or already handed to a match).</summary>
        public int LobbyConnections;

        /// <summary>Tickets in all queues now.</summary>
        public int QueuedPlayers;

        public long Joins;
        public long Cancels;

        /// <summary>Tickets removed because their connection closed or timed out (§6.2).</summary>
        public long DroppedTickets;

        public long RejectedJoins;
        public long MaintenanceDrops;

        public long MatchesAssembled;

        /// <summary>Matches assembled by the bot-fill timer with free slots (§6.2).</summary>
        public long BotFilledMatches;

        public long FillBots;

        /// <summary>Updates in which a match was due but <c>MaxMatches</c> rooms were open.</summary>
        public long NoFreeRoom;

        public long StatusesSent;
        public long MalformedMessages;
        public long ReliableSendFailures;

        /// <summary>Longest wait of a ticket until its match was assembled, microseconds.</summary>
        public long MaxWaitMicros;
    }
}
