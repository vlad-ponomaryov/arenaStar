namespace Arena.Net.Transport
{
    /// <summary>
    /// A token bucket in fixed point: <c>ratePerSecond</c> tokens per second, burst = one second's worth. Integer arithmetic,
    /// no allocations. Used for per-connection packet/byte limits and the per-IP handshake limiter (§3.2 "Лимиты").
    /// </summary>
    public struct TokenBucket
    {
        private const long MicrosPerSecond = 1000000L;

        /// <summary>Tokens × 10^6 (one token per second of rate = 1 per microsecond).</summary>
        private long _scaledTokens;
        private long _lastMicros;
        private bool _started;

        public void Reset()
        {
            _scaledTokens = 0L;
            _lastMicros = 0L;
            _started = false;
        }

        /// <summary>Takes <paramref name="cost"/> tokens if available. A new bucket starts full.</summary>
        public bool TryTake(long nowMicros, int ratePerSecond, int cost)
        {
            long capacity = (long)ratePerSecond * MicrosPerSecond;
            if (!_started)
            {
                _started = true;
                _scaledTokens = capacity;
                _lastMicros = nowMicros;
            }
            else if (nowMicros > _lastMicros)
            {
                long elapsed = nowMicros - _lastMicros;
                _lastMicros = nowMicros;
                long refill = elapsed * ratePerSecond;
                _scaledTokens = refill >= capacity - _scaledTokens ? capacity : _scaledTokens + refill;
            }
            long need = (long)cost * MicrosPerSecond;
            if (_scaledTokens < need) return false;
            _scaledTokens -= need;
            return true;
        }
    }
}
