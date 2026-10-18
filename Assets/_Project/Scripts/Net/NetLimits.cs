namespace Arena.Net
{
    /// <summary>
    /// Technical capacities of the network layer (Server-Spec §13, "L"): buffer and ring sizes, preallocated once — like
    /// <c>SimLimits</c> (D-004), not gameplay parameters. Tunable network settings live in <c>server.json</c> / <c>NetConfig</c>.
    /// </summary>
    public static class NetLimits
    {
        /// <summary>Largest datagram we send or accept: IPv6 minimum MTU (1280) minus IP/UDP headers (§3.2).</summary>
        public const int MaxPacketBytes = 1200;

        /// <summary>Receive buffer; anything longer than <see cref="MaxPacketBytes"/> is dropped (§3.2).</summary>
        public const int ReceiveBufferBytes = 2048;

        /// <summary>Largest reliable message — there is no fragmentation (D-082).</summary>
        public const int MaxReliableMessageBytes = 1000;

        /// <summary>OS send/receive buffer requested for UDP sockets (bursts of a whole server tick).</summary>
        public const int SocketBufferBytes = 1 << 18;

        /// <summary>Byte ring per connection for unacknowledged reliable messages.</summary>
        public const int ReliableBufferBytes = 8192;

        /// <summary>Reliable messages in flight per connection.</summary>
        public const int ReliableWindow = 64;

        /// <summary>Snapshot "views" kept per client for delta baselines (≈ 1 s at 30 Hz).</summary>
        public const int SnapshotHistory = 32;

        /// <summary>Per-slot input ring on the server, in ticks (a power of two).</summary>
        public const int InputBufferTicks = 64;

        /// <summary>Sent packets remembered for acknowledgements (sequence, send time); must exceed 33 (ack + 32 bits).</summary>
        public const int SentPacketHistory = 256;

        /// <summary>Addresses tracked by the per-IP handshake/ping limiter; the least recently used entry is replaced.</summary>
        public const int HandshakeIpTableSize = 1024;

        /// <summary>Endpoints cached by the portable server socket to send without allocating an <c>IPEndPoint</c> per packet.</summary>
        public const int EndPointCacheSize = 256;

        /// <summary>Late button presses carried over to the next tick where they make an edge (§2.4).</summary>
        public const int MaxPendingPresses = 3;

        /// <summary>Ticks the input log holds beyond the longest duration of the mode (overtime of a hold countdown, slack).</summary>
        public const int InputLogExtraTicks = 600;

        /// <summary>Inputs one <c>Input</c> message can carry (the newest and the redundant older ones, §2.4).</summary>
        public const int MaxInputsPerPacket = 1 << Protocol.NetProtocol.InputCountBits;

        /// <summary>Margins of received input packets remembered per slot, in ticks (≥ <c>MarginWindowTicks</c>, a power of two).</summary>
        public const int MarginHistoryTicks = 64;

        // ---- Snapshot "views" (§4.4, S3.5): capacities of the compact lists of a SnapshotFrame ----

        /// <summary>Projectile segments (live and recently ended) in one view; the nearest win (a full packet holds ≈ 90).</summary>
        public const int MaxSnapshotProjectiles = 64;

        /// <summary>Areas (zones, mines, fields) in one view.</summary>
        public const int MaxSnapshotAreas = 32;

        /// <summary>Pickups (acorns, berries) in one view = every pickup slot of the simulation.</summary>
        public const int MaxSnapshotPickups = 64;

        /// <summary>Statuses of the own fighter in one view.</summary>
        public const int MaxSnapshotOwnStatuses = 16;

        /// <summary>Events one decoded snapshot can carry (≥ the server setting <c>MaxEventsPerSnapshot</c>).</summary>
        public const int MaxSnapshotEvents = 64;

        /// <summary>Ticks of view events kept by the server's event journal (≥ redundancy × snapshot rate divisor).</summary>
        public const int EventJournalTicks = 16;

        /// <summary>View events journaled per tick; the rest of a tick is dropped and counted.</summary>
        public const int MaxJournalEventsPerTick = 64;
    }
}
