using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>Server → client: the connection is refused (§3.2). The nonce ties it to the client's attempt.</summary>
    public struct ConnectRejectedMessage
    {
        public ulong Nonce;
        public RejectReason Reason;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt64(Nonce);
            writer.WriteByte((byte)Reason);
        }

        public static bool Read(BitReader reader, out ConnectRejectedMessage message)
        {
            message.Nonce = reader.ReadUInt64();
            message.Reason = (RejectReason)reader.ReadByte();
            return !reader.IsOverflow && message.Reason != RejectReason.None && message.Reason <= RejectReason.MatchOver;
        }
    }
}
