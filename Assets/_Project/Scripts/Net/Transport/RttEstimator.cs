namespace Arena.Net.Transport
{
    /// <summary>
    /// Smoothed RTT and the retransmission timeout after RFC 6298: SRTT and RTTVAR with gains 1/8 and 1/4,
    /// RTO = max(SRTT + 4·RTTVAR, MinRto), at most MaxRto; before the first sample — InitialRto. Microseconds.
    /// </summary>
    public sealed class RttEstimator
    {
        // RFC 6298 constants (alpha = 1/8, beta = 1/4, K = 4) as shifts.
        private const int AlphaShift = 3;
        private const int BetaShift = 2;
        private const int VarianceMultiplier = 4;

        private long _minRto;
        private long _maxRto;
        private long _initialRto;

        public RttEstimator(long minRtoMicros, long maxRtoMicros, long initialRtoMicros)
        {
            Configure(minRtoMicros, maxRtoMicros, initialRtoMicros);
        }

        public bool HasSample { get; private set; }

        public long SmoothedMicros { get; private set; }

        public long VarianceMicros { get; private set; }

        public long LatestMicros { get; private set; }

        public void Configure(long minRtoMicros, long maxRtoMicros, long initialRtoMicros)
        {
            _minRto = minRtoMicros;
            _maxRto = maxRtoMicros < minRtoMicros ? minRtoMicros : maxRtoMicros;
            _initialRto = initialRtoMicros;
        }

        public void Reset()
        {
            HasSample = false;
            SmoothedMicros = 0L;
            VarianceMicros = 0L;
            LatestMicros = 0L;
        }

        public void AddSample(long rttMicros)
        {
            if (rttMicros < 0L) rttMicros = 0L;
            LatestMicros = rttMicros;
            if (!HasSample)
            {
                HasSample = true;
                SmoothedMicros = rttMicros;
                VarianceMicros = rttMicros / 2;
                return;
            }
            long error = SmoothedMicros - rttMicros;
            if (error < 0L) error = -error;
            VarianceMicros += (error - VarianceMicros) >> BetaShift;
            SmoothedMicros += (rttMicros - SmoothedMicros) >> AlphaShift;
        }

        public long RtoMicros
        {
            get
            {
                long rto = HasSample ? SmoothedMicros + VarianceMultiplier * VarianceMicros : _initialRto;
                if (rto < _minRto) rto = _minRto;
                if (rto > _maxRto) rto = _maxRto;
                return rto;
            }
        }
    }
}
