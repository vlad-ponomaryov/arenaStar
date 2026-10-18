namespace Arena.Net.Protocol
{
    /// <summary>
    /// First byte of every application message (Server-Spec §4.2): of a reliable message and of the unreliable payload of a data
    /// packet. Values are part of the wire format; 0 is invalid. Lobby messages (<c>QueueJoin</c> …, S3.8) take the next values.
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
    }
}
