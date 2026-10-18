using Arena.Core.Random;

namespace Arena.Net.Transport
{
    /// <summary>Seeded random values for tests (repeatable runs). Never use it in a real endpoint: ids would be predictable.</summary>
    public sealed class DeterministicNetRandom : INetRandom
    {
        private DeterministicRng _rng;

        public DeterministicNetRandom(int seed)
        {
            _rng = DeterministicRng.FromSeed(seed);
        }

        public ulong NextULong() => _rng.NextULong();
    }
}
