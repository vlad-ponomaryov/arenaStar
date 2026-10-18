using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>Ping</c> (client → server) and <c>Pong</c> (the echo) — region ping measurement (§4.2). Both packets have the same
    /// size, <see cref="NetProtocol.PingPacketBytes"/>; the client measures the time itself, by the nonce.
    /// </summary>
    public struct PingMessage
    {
        public uint ProtocolId;
        public ulong Nonce;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt32(ProtocolId);
            writer.WriteUInt64(Nonce);
        }

        public static bool Read(BitReader reader, out PingMessage message)
        {
            message.ProtocolId = reader.ReadUInt32();
            message.Nonce = reader.ReadUInt64();
            return !reader.IsOverflow;
        }
    }
}
