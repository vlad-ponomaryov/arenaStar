namespace Arena.Net.Protocol
{
    /// <summary>
    /// First byte of every application message (Server-Spec §4.2): of a reliable message and of the unreliable payload of a data
    /// packet. Values are part of the wire format; 0 is invalid. Lobby messages (8 …) came with S3.8 (protocol v3).
    /// </summary>
    public enum MessageKind : byte
    {
        Invalid = 0,

        /// <summary>Reliable, server → client: map, mode, slot table, own slot, countdown, snapshot rate (<see cref="MatchStartMessage"/>).</summary>
        MatchStart = 1,

        /// <summary>Reliable, client → server: the match scene is loaded (no body).</summary>
        ClientReady = 2,

        /// <summary>Unreliable, client → server, every prediction tick (<see cref="InputMessage"/>).</summary>
        Input = 3,

        /// <summary>Unreliable, server → client: a snapshot (<c>SnapshotEncoder</c>) follows the kind byte.</summary>
        Snapshot = 4,

        /// <summary>Reliable, server → client: the match result (<see cref="MatchEndedMessage"/>).</summary>
        MatchEnded = 5,

        /// <summary>Reliable, server → client: Last Beast — the own fighter is out, place and stats (<see cref="EliminatedMessage"/>).</summary>
        Eliminated = 6,

        /// <summary>Reliable, client → server: the player left the match himself — a bot takes over at once (no body).</summary>
        Leave = 7,

        /// <summary>Reliable, client → server: join the queue of a mode with a fighter (<see cref="QueueJoinMessage"/>).</summary>
        QueueJoin = 8,

        /// <summary>Unreliable, server → client, once a second while queued (<see cref="QueueStatusMessage"/>).</summary>
        QueueStatus = 9,

        /// <summary>Reliable, client → server: leave the queue (no body).</summary>
        QueueCancel = 10,

        /// <summary>Reliable, server → client: a match was assembled — the connection now belongs to it (<see cref="MatchAssignedMessage"/>).</summary>
        MatchAssigned = 11,

        /// <summary>Reliable, server → client: <c>QueueJoin</c> refused or the ticket dropped (<see cref="QueueRejectedMessage"/>).</summary>
        QueueRejected = 12,

        /// <summary>Reliable, server → client: maintenance or shutdown of the server (<see cref="ServerNoticeMessage"/>).</summary>
        ServerNotice = 13,
    }
}
