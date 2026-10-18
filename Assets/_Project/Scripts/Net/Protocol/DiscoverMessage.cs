using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>LAN discovery request (broadcast), padded to <see cref="NetProtocol.DiscoverPacketBytes"/> (§3.2, §4.2).</summary>
    public struct DiscoverMessage
    {
        public uint ProtocolId;
        public ulong Nonce;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt32(ProtocolId);
            writer.WriteUInt64(Nonce);
        }

        public static bool Read(BitReader reader, out DiscoverMessage message)
        {
            message.ProtocolId = reader.ReadUInt32();
            message.Nonce = reader.ReadUInt64();
            return !reader.IsOverflow;
        }
    }
}
