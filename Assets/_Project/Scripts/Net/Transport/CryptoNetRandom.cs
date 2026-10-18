using System.Security.Cryptography;

namespace Arena.Net.Transport
{
    /// <summary>Cryptographic random values (<see cref="RandomNumberGenerator"/>), drawn in blocks into a preallocated buffer.</summary>
    public sealed class CryptoNetRandom : INetRandom
    {
        private const int BlockValues = 32;

        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private readonly byte[] _block = new byte[BlockValues * 8];
        private int _next = BlockValues;

        public ulong NextULong()
        {
            if (_next == BlockValues)
            {
                _rng.GetBytes(_block);
                _next = 0;
            }
            int at = _next++ * 8;
            ulong v = 0ul;
            for (int i = 7; i >= 0; i--) v = (v << 8) | _block[at + i];
            return v;
        }
    }
}
