using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// Step 3, client → server, padded to <see cref="NetProtocol.HandshakePacketBytes"/>. Repeats the versions (the server
    /// kept no state after the request), returns the cookie and may carry a token (match ticket or <c>rejoinToken</c>).
    /// </summary>
    public struct ConnectResponseMessage
    {
        public uint ProtocolId;
        public ushort ProtocolVersion;
        public ulong ContentHash;
        public ConnectPurpose Purpose;
        public ulong Nonce;
        public ulong Cookie;
        public bool HasToken;
        public NetToken Token;

        public void Write(BitWriter writer)
        {
            writer.WriteUInt32(ProtocolId);
            writer.WriteUInt16(ProtocolVersion);
            writer.WriteUInt64(ContentHash);
            writer.WriteByte((byte)Purpose);
            writer.WriteUInt64(Nonce);
            writer.WriteUInt64(Cookie);
            writer.WriteBool(HasToken);
            if (HasToken)
            {
                writer.WriteUInt64(Token.High);
                writer.WriteUInt64(Token.Low);
            }
        }

        public static bool Read(BitReader reader, out ConnectResponseMessage message)
        {
            message.ProtocolId = reader.ReadUInt32();
            message.ProtocolVersion = reader.ReadUInt16();
            message.ContentHash = reader.ReadUInt64();
            message.Purpose = (ConnectPurpose)reader.ReadByte();
            message.Nonce = reader.ReadUInt64();
            message.Cookie = reader.ReadUInt64();
            message.HasToken = reader.ReadBool();
            if (message.HasToken)
            {
                ulong high = reader.ReadUInt64();
                ulong low = reader.ReadUInt64();
                message.Token = new NetToken(high, low);
            }
            else
            {
                message.Token = default;
            }
            return !reader.IsOverflow && message.Purpose <= ConnectPurpose.Rejoin;
        }
    }
}
