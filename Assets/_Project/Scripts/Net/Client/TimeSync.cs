using System;
using Arena.Core.Sim;

namespace Arena.Net.Client
{
    /// <summary>
    /// The prediction clock (Server-Spec §2.3, D-079): <see cref="PredictionTick"/> is the next tick the client predicts and sends
    /// input for. The server reports <c>InputMarginMin</c> (minimum of "newest input tick − T" over its window); the client keeps
    /// it at <see cref="ClientMatchSettings.TargetInputMarginTicks"/>: error <c>e = margin − target</c>; <c>|e| ≥ HardResyncTicks</c>
    /// — the tick jumps by −e at once (the input history is kept: ticks already sent keep their input, D-096); otherwise ticks run at
    /// <c>1 + clamp(−e × MarginGain, ±MaxTimeDilation)</c> of real time. After a start or a jump the reported margins still describe
    /// the old clock for a while, so feedback waits until the server's tick has passed that point (no repeated jumps).
    /// Start: <c>newest server tick + ceil(SRTT / dt) + target</c> (also used when joining a running match): the snapshot is half a
    /// round trip old and our input needs another half to arrive (D-096 corrects §2.3, which said SRTT / 2). Time comes in as
    /// <c>now</c> in microseconds — no clocks inside (virtual time in tests). No allocations.
    /// </summary>
    public sealed class TimeSync
    {
        private const long MicrosPerSecond = 1000000L;
        private const long MicrosPerMs = 1000L;
        private const double TickMicros = (double)MicrosPerSecond / SimLimits.TickRate;

        private readonly ClientMatchSettings _settings;
        private double _accumulator;
        private long _lastNow;
        private int _feedbackFromServerTick;

        public TimeSync(ClientMatchSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool Started { get; private set; }

        /// <summary>The next tick to predict (its input goes to the server).</summary>
        public int PredictionTick { get; private set; }

        /// <summary>Current rate of prediction ticks relative to real time (1 ± <see cref="ClientMatchSettings.MaxTimeDilation"/>).</summary>
        public float Rate { get; private set; } = 1f;

        /// <summary>The last margin error used (ticks), for statistics and the network overlay.</summary>
        public int LastMarginError { get; private set; }

        /// <summary>Newest server tick seen in a snapshot.</summary>
        public int NewestServerTick { get; private set; }

        public long Jumps { get; private set; }

        /// <summary>Fraction of the next tick already accumulated (0…1) — interpolation alpha of the own fighter's view.</summary>
        public float Alpha => (float)(_accumulator - Math.Floor(_accumulator));

        /// <summary>Starts the clock from the first snapshot (or a rejoin): <paramref name="rttMicros"/> — the transport's SRTT, ≤ 0 if none yet.</summary>
        public void Start(int newestServerTick, long rttMicros, long nowMicros)
        {
            int rttTicks = RttTicks(rttMicros);
            NewestServerTick = newestServerTick;
            PredictionTick = newestServerTick + rttTicks + _settings.TargetInputMarginTicks;
            _accumulator = 0.0;
            _lastNow = nowMicros;
            Rate = 1f;
            LastMarginError = 0;
            // Our first inputs reach the server about one RTT after this snapshot was made; margins before that are not ours.
            _feedbackFromServerTick = newestServerTick + rttTicks + _settings.TargetInputMarginTicks;
            Started = true;
        }

        /// <summary>
        /// Stops the clock: the next snapshot starts it again (<see cref="Start"/>) — after a rejoin or a long pause of the app
        /// (S3.12: a big gap is not caught up tick by tick, the clock jumps).
        /// </summary>
        public void Reset()
        {
            Started = false;
            _accumulator = 0.0;
            Rate = 1f;
            LastMarginError = 0;
        }

        /// <summary>A newer snapshot: margin feedback (late snapshots are ignored by tick).</summary>
        public void OnSnapshot(int serverTick, int inputMarginMin, long rttMicros)
        {
            if (Started && serverTick <= NewestServerTick) return;
            NewestServerTick = serverTick;
            if (!Started) return;
            if (serverTick < _feedbackFromServerTick)
            {
                Rate = 1f;
                return;
            }
            int e = inputMarginMin - _settings.TargetInputMarginTicks;
            LastMarginError = e;
            if (e >= _settings.HardResyncTicks || -e >= _settings.HardResyncTicks)
            {
                Jump(PredictionTick - e, serverTick, rttMicros);
                return;
            }
            float dilation = -e * _settings.MarginGain;
            float max = _settings.MaxTimeDilation;
            if (dilation > max) dilation = max;
            else if (dilation < -max) dilation = -max;
            Rate = 1f + dilation;
        }

        /// <summary>Moves the prediction tick to <paramref name="tick"/> now (hard resync, re-simulation limit).</summary>
        public void JumpTo(int tick, long rttMicros) => Jump(tick, NewestServerTick, rttMicros);

        /// <summary>Real time passed: how many prediction ticks are due in this frame (≤ <see cref="ClientMatchSettings.MaxTicksPerFrame"/>).</summary>
        public int Advance(long nowMicros)
        {
            if (!Started) return 0;
            long dt = nowMicros - _lastNow;
            _lastNow = nowMicros;
            if (dt > 0) _accumulator += dt * (double)Rate / TickMicros;
            int due = (int)Math.Floor(_accumulator);
            int max = _settings.MaxTicksPerFrame;
            if (due > max) due = max;
            if (due < 0) due = 0;
            _accumulator -= due;
            // A long hitch is caught up over a few frames, but never more than a resync's worth (then the margin jumps it).
            double cap = _settings.HardResyncTicks;
            if (_accumulator > cap) _accumulator = cap;
            return due;
        }

        /// <summary>The tick <see cref="PredictionTick"/> was predicted and sent: move to the next.</summary>
        public void CompleteTick() => PredictionTick++;

        private void Jump(int tick, int serverTick, long rttMicros)
        {
            PredictionTick = tick;
            _accumulator -= Math.Floor(_accumulator);
            Rate = 1f;
            _feedbackFromServerTick = serverTick + RttTicks(rttMicros) + _settings.MarginSettleTicks;
            Jumps++;
        }

        private int RttTicks(long rttMicros)
        {
            if (rttMicros <= 0) rttMicros = _settings.FallbackRttMs * MicrosPerMs;
            return (int)Math.Ceiling(rttMicros / TickMicros);
        }
    }
}
