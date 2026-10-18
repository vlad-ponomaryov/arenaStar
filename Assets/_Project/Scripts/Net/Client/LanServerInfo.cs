using Arena.Net.Transport;

namespace Arena.Net.Client
{
    /// <summary>A server found by <see cref="LanDiscovery"/> (Server-Spec §3.2, §6.3): its address and its <c>DiscoverReply</c>.</summary>
    public struct LanServerInfo
    {
        public NetAddress Address;
        public ushort ProtocolVersion;
        public ulong ContentHash;

        /// <summary>Same protocol version and content hash as this build — joinable (otherwise "update the game", §4.1).</summary>
        public bool Compatible;

        /// <summary>Localization key index of the server name (no free text, GDD §2).</summary>
        public ushort ServerNameKey;

        public int Connections;
        public int Capacity;
        public bool Maintenance;

        /// <summary>Time from the first broadcast to the first answer, microseconds (a rough ping).</summary>
        public long ReplyMicros;
    }
}
