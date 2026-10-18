namespace Arena.Net.Transport
{
    /// <summary>
    /// Stateless handshake cookies (§3.2): cookie = SipHash-2-4(server key, client address ‖ port ‖ nonce ‖ time slice).
    /// The server remembers nothing until the client returns a valid cookie from the same address, which proves the address
    /// (no spoofing). A cookie is valid in its slice and the next one. The key is random per server process.
    /// </summary>
    public sealed class Handshake
    {
        private const int InputBytes = 16 + 2 + 8 + 8;

        private readonly ulong _k0;
        private readonly ulong _k1;
        private readonly byte[] _input = new byte[InputBytes];

        public Handshake(INetRandom random)
        {
            _k0 = random.NextULong();
            _k1 = random.NextULong();
        }

        public static long Slice(long nowMicros, int sliceMs) => nowMicros / ((long)sliceMs * 1000L);

        public ulong Cookie(in NetAddress client, ulong nonce, long slice)
        {
            client.WriteIPv6Bytes(_input, 0);
            _input[16] = (byte)client.Port;
            _input[17] = (byte)(client.Port >> 8);
            WriteUInt64(18, nonce);
            WriteUInt64(26, (ulong)slice);
            return SipHash.Compute(_k0, _k1, _input, 0, InputBytes);
        }

        public ulong CurrentCookie(in NetAddress client, ulong nonce, long nowMicros, int sliceMs) =>
            Cookie(client, nonce, Slice(nowMicros, sliceMs));

        public bool Verify(in NetAddress client, ulong nonce, ulong cookie, long nowMicros, int sliceMs)
        {
            long slice = Slice(nowMicros, sliceMs);
            return Cookie(client, nonce, slice) == cookie || Cookie(client, nonce, slice - 1) == cookie;
        }

        private void WriteUInt64(int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) _input[offset + i] = (byte)(value >> (8 * i));
        }
    }
}
