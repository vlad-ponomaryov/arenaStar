using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// Step 4, server → client. The connection id is in the packet header. Carries the transport parameters the client must
    /// follow (keepalive, timeout, the server's per-connection limits). Match parameters (snapshot rate, input redundancy) come
    /// with <c>MatchStart</c> (§4.2). A repeated <c>ConnectResponse</c> with the same nonce gets the same message again.
    /// </summary>
    public struct ConnectAcceptedMessage
    {
        public ulong Nonce;
        public uint KeepaliveMs;
        public uint DisconnectTimeoutMs;
        public uint PacketsPerSecond;
        public uint BytesPerSecond;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt64(Nonce);
            writer.WriteVarUInt(KeepaliveMs);
            writer.WriteVarUInt(DisconnectTimeoutMs);
            writer.WriteVarUInt(PacketsPerSecond);
            writer.WriteVarUInt(BytesPerSecond);
        }

        public static bool Read(BitReader reader, out ConnectAcceptedMessage message)
        {
            message.Nonce = reader.ReadUInt64();
            message.KeepaliveMs = reader.ReadVarUInt();
            message.DisconnectTimeoutMs = reader.ReadVarUInt();
            message.PacketsPerSecond = reader.ReadVarUInt();
            message.BytesPerSecond = reader.ReadVarUInt();
            return !reader.IsOverflow;
        }
    }
}
