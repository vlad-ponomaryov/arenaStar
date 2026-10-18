namespace Arena.Net.Transport
{
    /// <summary>Comparison of 16-bit wrapping sequence numbers (packets, reliable messages).</summary>
    public static class Sequence16
    {
        private const int Half = 32768;

        /// <summary>True if <paramref name="a"/> is newer than <paramref name="b"/> (within half the range).</summary>
        public static bool IsNewer(ushort a, ushort b) =>
            (a > b && a - b <= Half) || (a < b && b - a > Half);

        /// <summary>How far <paramref name="newer"/> is ahead of <paramref name="older"/>, modulo 2^16.</summary>
        public static int Distance(ushort newer, ushort older) => (ushort)(newer - older);
    }
}
