using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// Step 1 of the handshake, client → server, connectionless (Server-Spec §3.2). The packet is padded to
    /// <see cref="NetProtocol.HandshakePacketBytes"/>; the server answers with a smaller <c>ConnectChallenge</c> or
    /// <c>ConnectRejected</c> and remembers nothing.
    /// </summary>
    public struct ConnectRequestMessage
    {
        public uint ProtocolId;
        public ushort ProtocolVersion;
        public ulong ContentHash;
        public ConnectPurpose Purpose;
        public ulong Nonce;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt32(ProtocolId);
            writer.WriteUInt16(ProtocolVersion);
            writer.WriteUInt64(ContentHash);
            writer.WriteByte((byte)Purpose);
            writer.WriteUInt64(Nonce);
        }

        /// <summary>False if the data is truncated or malformed (the packet is dropped).</summary>
        public static bool Read(BitReader reader, out ConnectRequestMessage message)
        {
            message.ProtocolId = reader.ReadUInt32();
            message.ProtocolVersion = reader.ReadUInt16();
            message.ContentHash = reader.ReadUInt64();
            message.Purpose = (ConnectPurpose)reader.ReadByte();
            message.Nonce = reader.ReadUInt64();
            return !reader.IsOverflow && message.Purpose <= ConnectPurpose.Rejoin;
        }
    }
}
