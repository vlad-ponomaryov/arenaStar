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

        /// <summary>Byte ring per connection for unacknowledged reliable messages.</summary>
        public const int ReliableBufferBytes = 8192;

        /// <summary>Reliable messages in flight per connection.</summary>
        public const int ReliableWindow = 64;

        /// <summary>Snapshot "views" kept per client for delta baselines (≈ 1 s at 30 Hz).</summary>
        public const int SnapshotHistory = 32;

        /// <summary>Per-slot input ring on the server, in ticks.</summary>
        public const int InputBufferTicks = 64;

        /// <summary>Late button presses carried over to the next tick where they make an edge (§2.4).</summary>
        public const int MaxPendingPresses = 3;
    }
}
