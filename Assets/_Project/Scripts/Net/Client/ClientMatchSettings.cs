namespace Arena.Net.Client
{
    /// <summary>
    /// Client match parameters (Server-Spec §2.3, §2.5, §13 — the "C" rows): prediction clock, re-simulation and smoothing.
    /// Values of <see cref="CreateDefault"/> are the PROTO defaults of §13; the game client reads them from <c>NetConfig</c>
    /// (S3.10), tests and the server self-test use the defaults.
    /// </summary>
    public sealed class ClientMatchSettings
    {
        /// <summary>The client keeps the server's <c>InputMarginMin</c> at this many ticks.</summary>
        public int TargetInputMarginTicks;

        /// <summary>Tick-rate change per tick of margin error (0.02 = 2 % per tick).</summary>
        public float MarginGain;

        /// <summary>Largest tick-rate change, ± (0.05 = 5 %).</summary>
        public float MaxTimeDilation;

        /// <summary>A margin error of at least this many ticks makes the prediction tick jump at once (the input history is reset).</summary>
        public int HardResyncTicks;

        /// <summary>
        /// After a jump the margins still reported by the server come from the old clock for the server's margin window
        /// (<c>MarginWindowTicks</c>, 30) plus a round trip: margin feedback waits this many server ticks + the RTT in ticks.
        /// </summary>
        public int MarginSettleTicks;

        /// <summary>At most this many ticks are re-simulated on a snapshot (Tpred − Ts); more — the clock jumps back to the limit.</summary>
        public int MaxResimTicks;

        /// <summary>Most prediction ticks run in one frame (a longer hitch is caught up over the next frames).</summary>
        public int MaxTicksPerFrame;

        /// <summary>RTT assumed before the transport has a sample (the first prediction tick estimate).</summary>
        public int FallbackRttMs;

        /// <summary>The correction offset of the own fighter decays exponentially with this time constant.</summary>
        public float CorrectionSmoothingSeconds;

        /// <summary>A correction longer than this (cells) is not smoothed: the view is placed at once.</summary>
        public float SnapDistanceCells;

        /// <summary>A prediction error above this (cells) counts as a large correction in the statistics (Server-Spec §11.1).</summary>
        public float LargeCorrectionCells;

        /// <summary>Interpolation delay in snapshot intervals (before the jitter part), Server-Spec §2.3.</summary>
        public float InterpDelaySnapshots;

        /// <summary>The interpolation delay adds this many times the arrival jitter (EMA of |interval − nominal|, ticks).</summary>
        public float InterpJitterFactor;

        /// <summary>Weight of a new sample in the arrival jitter EMA (0…1).</summary>
        public float InterpJitterSmoothing;

        /// <summary>Render-clock rate change per tick of error (0.02 = 2 % per tick), clamped by <see cref="MaxTimeDilation"/>.</summary>
        public float InterpGain;

        /// <summary>The interpolation delay never exceeds this (ms); the server caps the reported delay the same way (§13).</summary>
        public int InterpDelayMaxMs;

        /// <summary>Without newer data a fighter is extrapolated by its velocity at most this long (ms), then it stands.</summary>
        public int MaxExtrapolationMs;

        /// <summary>Send <c>ClientStats</c> (prediction and presentation counters, §5.6) every this many prediction ticks; 0 — never.</summary>
        public int StatsReportTicks;

        /// <summary>Answer <c>MatchStart</c> with <c>ClientReady</c> at once (tests, self-test); the game sends it after the scene loads.</summary>
        public bool ReadyOnMatchStart;

        public static ClientMatchSettings CreateDefault() => new ClientMatchSettings
        {
            TargetInputMarginTicks = 1,
            MarginGain = 0.02f,
            MaxTimeDilation = 0.05f,
            HardResyncTicks = 10,
            MarginSettleTicks = 30,
            MaxResimTicks = 15,
            MaxTicksPerFrame = 5,
            FallbackRttMs = 150,
            CorrectionSmoothingSeconds = 0.1f,
            SnapDistanceCells = 3f,
            LargeCorrectionCells = 0.25f,
            InterpDelaySnapshots = 2f,
            InterpJitterFactor = 2f,
            InterpJitterSmoothing = 0.1f,
            InterpGain = 0.02f,
            InterpDelayMaxMs = 200,
            MaxExtrapolationMs = 120,
            StatsReportTicks = 30,
            ReadyOnMatchStart = true
        };
    }
}
