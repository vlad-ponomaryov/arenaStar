using System;

namespace Arena.Server.Host
{
    /// <summary>
    /// Fixed-bucket histogram of durations in microseconds (tick work time, tick lateness — Server-Spec §5.3, §5.6): buckets of
    /// <see cref="BucketMicros"/> up to <see cref="RangeMicros"/>, larger values in the last bucket (the exact maximum is kept).
    /// Recording allocates nothing; single-threaded (the game thread), the metrics thread reads published numbers only.
    /// </summary>
    public sealed class LatencyHistogram
    {
        public const int BucketMicros = 10;
        public const int RangeMicros = 200000;

        private readonly long[] _buckets = new long[RangeMicros / BucketMicros + 1];
        private long _sum;

        public long Count { get; private set; }

        public long MaxMicros { get; private set; }

        public double MeanMicros => Count == 0 ? 0.0 : (double)_sum / Count;

        public void Record(long micros)
        {
            if (micros < 0) micros = 0;
            long index = micros / BucketMicros;
            if (index >= _buckets.Length) index = _buckets.Length - 1;
            _buckets[index]++;
            Count++;
            _sum += micros;
            if (micros > MaxMicros) MaxMicros = micros;
        }

        /// <summary>Upper edge of the bucket holding quantile <paramref name="q"/> (0…1), capped by the maximum; 0 when empty.</summary>
        public long PercentileMicros(double q)
        {
            if (Count == 0) return 0;
            if (q < 0.0) q = 0.0;
            if (q > 1.0) q = 1.0;
            long rank = (long)Math.Ceiling(q * Count);
            if (rank < 1) rank = 1;
            long seen = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                seen += _buckets[i];
                if (seen < rank) continue;
                long edge = (long)(i + 1) * BucketMicros;
                return edge < MaxMicros ? edge : MaxMicros;
            }
            return MaxMicros;
        }

        public void Reset()
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            _sum = 0;
            Count = 0;
            MaxMicros = 0;
        }
    }
}
