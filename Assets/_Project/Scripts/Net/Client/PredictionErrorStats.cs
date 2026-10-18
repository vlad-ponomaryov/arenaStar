namespace Arena.Net.Client
{
    /// <summary>
    /// Statistics of the own fighter's prediction error at reconciliation (Server-Spec §2.5, §11.1): the distance between the
    /// predicted and the server position on the snapshot tick. Samples caused by something the prediction cannot know (a push,
    /// a stun or slow, a hook pull, damage, a defeat — "external") are counted apart. Percentiles come from a fixed histogram
    /// (<see cref="NetLimits.PredictionErrorBins"/> bins of <see cref="NetLimits.PredictionErrorBinCells"/>), so adding a sample
    /// does not allocate.
    /// </summary>
    public sealed class PredictionErrorStats
    {
        private readonly long[] _bins = new long[NetLimits.PredictionErrorBins];
        private readonly float _largeCells;

        public PredictionErrorStats(float largeCorrectionCells)
        {
            _largeCells = largeCorrectionCells;
        }

        /// <summary>Samples without an external cause.</summary>
        public long Samples { get; private set; }

        public long ExternalSamples { get; private set; }

        /// <summary>Samples (without an external cause) with an error above zero.</summary>
        public long NonZeroSamples { get; private set; }

        /// <summary>Samples (without an external cause) above <see cref="ClientMatchSettings.LargeCorrectionCells"/>.</summary>
        public long LargeCorrections { get; private set; }

        public float MaxCells { get; private set; }

        public float LastCells { get; private set; }

        public void Add(float errorCells, bool external)
        {
            LastCells = errorCells;
            if (external)
            {
                ExternalSamples++;
                return;
            }
            Samples++;
            if (errorCells > 0f) NonZeroSamples++;
            if (errorCells > _largeCells) LargeCorrections++;
            if (errorCells > MaxCells) MaxCells = errorCells;
            int bin = (int)(errorCells / NetLimits.PredictionErrorBinCells);
            if (bin < 0) bin = 0;
            if (bin >= _bins.Length) bin = _bins.Length - 1;
            _bins[bin]++;
        }

        /// <summary>Upper edge (cells) of the bin that holds the <paramref name="fraction"/> quantile (0.99 = p99); 0 without samples.</summary>
        public float Percentile(double fraction)
        {
            if (Samples == 0) return 0f;
            long need = (long)System.Math.Ceiling(Samples * fraction);
            if (need < 1) need = 1;
            long seen = 0;
            for (int i = 0; i < _bins.Length; i++)
            {
                seen += _bins[i];
                if (seen >= need) return (i + 1) * NetLimits.PredictionErrorBinCells;
            }
            return MaxCells;
        }

        public void Clear()
        {
            for (int i = 0; i < _bins.Length; i++) _bins[i] = 0;
            Samples = ExternalSamples = NonZeroSamples = LargeCorrections = 0;
            MaxCells = LastCells = 0f;
        }
    }
}
