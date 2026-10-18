namespace Arena.Net.Transport
{
    /// <summary>
    /// SipHash-2-4 (Aumasson, Bernstein, 2012): a keyed 64-bit PRF for handshake cookies (§3.2) and, on stage 4, packet tags.
    /// Key = two little-endian 64-bit words. Checked against the reference test vectors. No allocations.
    /// </summary>
    public static class SipHash
    {
        // Initialization constants of the specification ("somepseudorandomlygeneratedbytes").
        private const ulong C0 = 0x736f6d6570736575ul;
        private const ulong C1 = 0x646f72616e646f6dul;
        private const ulong C2 = 0x6c7967656e657261ul;
        private const ulong C3 = 0x7465646279746573ul;
        private const ulong FinalizationXor = 0xfful;

        public static ulong Compute(ulong k0, ulong k1, byte[] data, int offset, int length)
        {
            ulong v0 = k0 ^ C0;
            ulong v1 = k1 ^ C1;
            ulong v2 = k0 ^ C2;
            ulong v3 = k1 ^ C3;
            int end = offset + length - (length & 7);
            int i = offset;
            for (; i < end; i += 8)
            {
                ulong m = ReadUInt64(data, i);
                v3 ^= m;
                Round(ref v0, ref v1, ref v2, ref v3);
                Round(ref v0, ref v1, ref v2, ref v3);
                v0 ^= m;
            }
            ulong b = (ulong)(uint)length << 56;
            int rest = length & 7;
            for (int j = 0; j < rest; j++) b |= (ulong)data[i + j] << (8 * j);
            v3 ^= b;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            v0 ^= b;
            v2 ^= FinalizationXor;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            return v0 ^ v1 ^ v2 ^ v3;
        }

        private static void Round(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3)
        {
            v0 += v1;
            v1 = Rotl(v1, 13);
            v1 ^= v0;
            v0 = Rotl(v0, 32);
            v2 += v3;
            v3 = Rotl(v3, 16);
            v3 ^= v2;
            v0 += v3;
            v3 = Rotl(v3, 21);
            v3 ^= v0;
            v2 += v1;
            v1 = Rotl(v1, 17);
            v1 ^= v2;
            v2 = Rotl(v2, 32);
        }

        private static ulong Rotl(ulong x, int b) => (x << b) | (x >> (64 - b));

        private static ulong ReadUInt64(byte[] data, int offset)
        {
            ulong v = 0ul;
            for (int i = 7; i >= 0; i--) v = (v << 8) | data[offset + i];
            return v;
        }
    }
}
