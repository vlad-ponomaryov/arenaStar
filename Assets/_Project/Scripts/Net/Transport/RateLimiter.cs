namespace Arena.Net.Transport
{
    /// <summary>
    /// Per-IP limiter of connectionless traffic (handshakes, pings, discovery; §3.2 "Лимиты"): a fixed table of
    /// <see cref="NetLimits.HandshakeIpTableSize"/> token buckets, open addressing by host hash with a short probe; when the
    /// probe finds no free entry, the least recently used one is replaced (an attacker cycling addresses only resets its own
    /// buckets). The rate is per minute. No allocations after construction.
    /// </summary>
    public sealed class RateLimiter
    {
        private const int Probe = 8;
        private const int SecondsPerMinute = 60;

        private readonly NetAddress[] _hosts = new NetAddress[NetLimits.HandshakeIpTableSize];
        private readonly bool[] _used = new bool[NetLimits.HandshakeIpTableSize];
        private readonly long[] _lastSeen = new long[NetLimits.HandshakeIpTableSize];
        private readonly TokenBucket[] _buckets = new TokenBucket[NetLimits.HandshakeIpTableSize];

        public long RejectedCount { get; private set; }

        /// <summary>
        /// One packet from the host of <paramref name="from"/>; false — over <paramref name="perMinute"/> (drop silently).
        /// The bucket is scaled to tokens per second × 60 so that the rate per minute stays an integer.
        /// </summary>
        public bool Allow(in NetAddress from, long nowMicros, int perMinute)
        {
            int mask = NetLimits.HandshakeIpTableSize - 1;
            int start = from.HostHash() & mask;
            int victim = start;
            long oldest = long.MaxValue;
            int slot = -1;
            for (int i = 0; i < Probe; i++)
            {
                int s = (start + i) & mask;
                if (_used[s] && _hosts[s].SameHost(from))
                {
                    slot = s;
                    break;
                }
                long age = _used[s] ? _lastSeen[s] : long.MinValue;
                if (age < oldest)
                {
                    oldest = age;
                    victim = s;
                }
            }
            if (slot < 0)
            {
                slot = victim;
                _used[slot] = true;
                _hosts[slot] = new NetAddress(from.High, from.Low, 0);
                _buckets[slot].Reset();
            }
            _lastSeen[slot] = nowMicros;
            // Time scaled ×60: a per-minute rate becomes a per-"second" rate of the bucket.
            if (_buckets[slot].TryTake(nowMicros / SecondsPerMinute, perMinute, 1)) return true;
            RejectedCount++;
            return false;
        }

        public void Clear()
        {
            System.Array.Clear(_used, 0, _used.Length);
            RejectedCount = 0;
        }
    }
}
