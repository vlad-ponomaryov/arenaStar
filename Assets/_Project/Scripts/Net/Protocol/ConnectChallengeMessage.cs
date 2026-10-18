using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>Step 2, server → client: a stateless cookie bound to the client's address, nonce and time slice (§3.2).</summary>
    public struct ConnectChallengeMessage
    {
        public ulong Nonce;
        public ulong Cookie;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt64(Nonce);
            writer.WriteUInt64(Cookie);
        }

        public static bool Read(BitReader reader, out ConnectChallengeMessage message)
        {
            message.Nonce = reader.ReadUInt64();
            message.Cookie = reader.ReadUInt64();
            return !reader.IsOverflow;
        }
    }
}
