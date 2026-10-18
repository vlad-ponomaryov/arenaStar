namespace Arena.Net.Serialization
{
    /// <summary>
    /// Variable-length integers (LEB128: 7 value bits per byte, high bit = "more") and zigzag mapping of signed values
    /// (0, -1, 1, -2 ... -> 0, 1, 2, 3 ...), so small negative ticks (countdown, ticks in the past) stay short (Server-Spec §4.3).
    /// </summary>
    public static class VarInt
    {
        /// <summary>Longest encoding of a 32-bit value, in bytes.</summary>
        public const int MaxBytes32 = 5;

        /// <summary>Longest encoding of a 64-bit value, in bytes.</summary>
        public const int MaxBytes64 = 10;

        public static uint ZigZagEncode(int value) => (uint)((value << 1) ^ (value >> 31));

        public static int ZigZagDecode(uint value) => (int)(value >> 1) ^ -(int)(value & 1u);

        public static ulong ZigZagEncode64(long value) => (ulong)((value << 1) ^ (value >> 63));

        public static long ZigZagDecode64(ulong value) => (long)(value >> 1) ^ -(long)(value & 1ul);

        /// <summary>Encoded size of <paramref name="value"/> in bytes (1...5).</summary>
        public static int ByteCount(uint value)
        {
            int n = 1;
            while (value >= 0x80u)
            {
                value >>= 7;
                n++;
            }
            return n;
        }

        /// <summary>Encoded size of <paramref name="value"/> in bytes (1...10).</summary>
        public static int ByteCount(ulong value)
        {
            int n = 1;
            while (value >= 0x80ul)
            {
                value >>= 7;
                n++;
            }
            return n;
        }

        public static int ByteCountSigned(int value) => ByteCount(VarInt.ZigZagEncode(value));
    }
}
