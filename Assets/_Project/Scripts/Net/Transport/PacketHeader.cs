using Arena.Net.Protocol;

namespace Arena.Net.Transport
{
    /// <summary>
    /// The 18-byte transport header (Server-Spec §3.2), little-endian, byte-aligned:
    /// <c>u8 kind|flags, u64 connectionId, u16 sequence, u16 ack, u32 ackBits, u8 ackDelay</c>.
    /// </summary>
    public struct PacketHeader
    {
        private const int KindMask = (1 << NetProtocol.PacketKindBits) - 1;

        public PacketKind Kind;
        public PacketFlags Flags;
        public ulong ConnectionId;
        public ushort Sequence;
        public ushort Ack;
        public uint AckBits;

        /// <summary>How long the newest acknowledged packet waited at the receiver, in <see cref="NetProtocol.AckDelayUnitMicros"/>.</summary>
        public byte AckDelay;

        /// <summary>The 64-bit challenge of <c>PathChallenge</c> / <c>PathResponse</c>, carried in sequence, ack and ackBits.</summary>
        public ulong PathData
        {
            get => Sequence | ((ulong)Ack << 16) | ((ulong)AckBits << 32);
            set
            {
                Sequence = (ushort)value;
                Ack = (ushort)(value >> 16);
                AckBits = (uint)(value >> 32);
            }
        }

        public static PacketHeader Connectionless(PacketKind kind) => new PacketHeader { Kind = kind };

        public void Write(byte[] buffer)
        {
            buffer[0] = (byte)(((int)Kind & KindMask) | ((int)Flags << NetProtocol.PacketKindBits));
            WriteUInt64(buffer, 1, ConnectionId);
            buffer[9] = (byte)Sequence;
            buffer[10] = (byte)(Sequence >> 8);
            buffer[11] = (byte)Ack;
            buffer[12] = (byte)(Ack >> 8);
            buffer[13] = (byte)AckBits;
            buffer[14] = (byte)(AckBits >> 8);
            buffer[15] = (byte)(AckBits >> 16);
            buffer[16] = (byte)(AckBits >> 24);
            buffer[17] = AckDelay;
        }

        /// <summary>False if the packet is shorter than the header or the kind is unknown.</summary>
        public static bool TryRead(byte[] buffer, int length, out PacketHeader header)
        {
            header = default;
            if (length < NetProtocol.HeaderBytes) return false;
            int kind = buffer[0] & KindMask;
            if (kind == (int)PacketKind.Invalid || kind > (int)PacketKind.PathResponse) return false;
            header.Kind = (PacketKind)kind;
            header.Flags = (PacketFlags)(buffer[0] >> NetProtocol.PacketKindBits);
            header.ConnectionId = ReadUInt64(buffer, 1);
            header.Sequence = (ushort)(buffer[9] | (buffer[10] << 8));
            header.Ack = (ushort)(buffer[11] | (buffer[12] << 8));
            header.AckBits = (uint)(buffer[13] | (buffer[14] << 8) | (buffer[15] << 16) | (buffer[16] << 24));
            header.AckDelay = buffer[17];
            return true;
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) buffer[offset + i] = (byte)(value >> (8 * i));
        }

        private static ulong ReadUInt64(byte[] buffer, int offset)
        {
            ulong v = 0ul;
            for (int i = 7; i >= 0; i--) v = (v << 8) | buffer[offset + i];
            return v;
        }
    }
}
