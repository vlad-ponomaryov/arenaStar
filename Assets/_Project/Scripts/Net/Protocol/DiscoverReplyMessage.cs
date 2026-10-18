using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// LAN discovery answer (§4.2): versions, content hash, the localization key index of the server's name (no free text,
    /// GDD §2), occupancy and maintenance. Same size as the request.
    /// </summary>
    public struct DiscoverReplyMessage
    {
        public uint ProtocolId;
        public ulong Nonce;
        public ushort ProtocolVersion;
        public ulong ContentHash;
        public ushort ServerNameKey;
        public ushort Connections;
        public ushort Capacity;
        public bool Maintenance;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt32(ProtocolId);
            writer.WriteUInt64(Nonce);
            writer.WriteUInt16(ProtocolVersion);
            writer.WriteUInt64(ContentHash);
            writer.WriteUInt16(ServerNameKey);
            writer.WriteUInt16(Connections);
            writer.WriteUInt16(Capacity);
            writer.WriteBool(Maintenance);
        }

        public static bool Read(BitReader reader, out DiscoverReplyMessage message)
        {
            message.ProtocolId = reader.ReadUInt32();
            message.Nonce = reader.ReadUInt64();
            message.ProtocolVersion = reader.ReadUInt16();
            message.ContentHash = reader.ReadUInt64();
            message.ServerNameKey = reader.ReadUInt16();
            message.Connections = reader.ReadUInt16();
            message.Capacity = reader.ReadUInt16();
            message.Maintenance = reader.ReadBool();
            return !reader.IsOverflow;
        }
    }
}
