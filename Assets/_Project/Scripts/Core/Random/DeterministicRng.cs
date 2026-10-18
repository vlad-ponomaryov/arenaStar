using System;

namespace Arena.Core.Random
{
    /// <summary>
    /// Deterministic xorshift128+ generator (D-005). A blittable struct that lives inside the match state, so snapshots and
    /// resimulation reproduce the same sequence. Always use it by reference (<c>ref state.Globals.Rng</c>): copying the
    /// struct forks the sequence.
    /// Note: inside <c>Arena.Core.*</c> the name <c>Random</c> resolves to this namespace, not to System.Random.
    /// </summary>
    public struct DeterministicRng : IEquatable<DeterministicRng>
    {
        private const float Float01Scale = 1f / 16777216f; // 2^-24: 24 random bits -> [0, 1)

        public ulong S0;
        public ulong S1;

        public DeterministicRng(ulong seed)
        {
            // SplitMix64 expands the seed into two well-mixed words; the all-zero state is invalid for xorshift.
            ulong x = seed;
            S0 = SplitMix64(ref x);
            S1 = SplitMix64(ref x);
            if (S0 == 0UL && S1 == 0UL) S1 = 1UL;
        }

        public static DeterministicRng FromSeed(int seed) => new DeterministicRng(unchecked((ulong)(uint)seed));

        public ulong NextULong()
        {
            unchecked
            {
                ulong s1 = S0;
                ulong s0 = S1;
                S0 = s0;
                s1 ^= s1 << 23;
                S1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
                return S1 + s0;
            }
        }

        /// <summary>Uniform 32-bit value (upper half of the 64-bit output — the strongest bits).</summary>
        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat01() => (NextUInt() >> 8) * Float01Scale;

        /// <summary>Uniform int in [minInclusive, maxExclusive); returns minInclusive if the range is empty.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)((long)maxExclusive - minInclusive);
            return (int)(minInclusive + (long)(((ulong)NextUInt() * span) >> 32));
        }

        /// <summary>Uniform float in [min, max); returns min if max ≤ min.</summary>
        public float Range(float min, float max)
        {
            if (max <= min) return min;
            float v = min + (max - min) * NextFloat01();
            return v < max ? v : min; // guards float rounding up to max
        }

        public bool Equals(DeterministicRng other) => S0 == other.S0 && S1 == other.S1;
        public override bool Equals(object obj) => obj is DeterministicRng other && Equals(other);
        public override int GetHashCode() => S0.GetHashCode() ^ (S1.GetHashCode() * 397);

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
