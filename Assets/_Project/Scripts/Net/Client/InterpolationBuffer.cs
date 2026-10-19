using System;
using Arena.Core.Sim;
using Arena.Net.Snapshots;

namespace Arena.Net.Client
{
    /// <summary>
    /// The render clock and the snapshot pair around it (Server-Spec §2.3, §2.6, S3.7b). <see cref="RenderTick"/> (fractional)
    /// follows <c>newest snapshot tick − delay</c>; the delay is <see cref="ClientMatchSettings.InterpDelaySnapshots"/> snapshot
    /// intervals + <see cref="ClientMatchSettings.InterpJitterFactor"/> × the arrival jitter (EMA of |arrival interval − nominal|),
    /// at most <see cref="ClientMatchSettings.InterpDelayMaxMs"/>. The clock advances once per prediction tick (the display is built
    /// tick by tick for <c>IMatchDriver</c>, D-097) at <c>1 + clamp(InterpGain × error, ±MaxTimeDilation)</c>; an error above
    /// <see cref="ClientMatchSettings.HardResyncTicks"/> jumps it — backwards only right after a new snapshot; without new data the
    /// clock never goes back and holds at <c>newest + MaxExtrapolationMs</c> (a stall must not replay the world, F2). <see cref="Sample"/> finds the newest frame at or before the
    /// render tick (<see cref="From"/>) and the next one (<see cref="To"/>) in the decoder's ring (late snapshots land there too);
    /// without a newer frame the render tick is ahead of the data: <see cref="Extrapolating"/>, capped at
    /// <see cref="ClientMatchSettings.MaxExtrapolationMs"/>. No allocations.
    /// </summary>
    public sealed class InterpolationBuffer
    {
        private const double TickMicros = 1000000.0 / SimLimits.TickRate;
        private const double MinAhead = 1e-6;

        private readonly ClientMatchSettings _settings;
        private readonly SnapshotFrameRing _ring;
        private long _lastArrivalMicros;
        private int _lastArrivalTick;
        private bool _hasArrival;
        private bool _fresh;

        public InterpolationBuffer(ClientMatchSettings settings, SnapshotFrameRing ring)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ring = ring ?? throw new ArgumentNullException(nameof(ring));
            SnapshotInterval = 1;
        }

        /// <summary>Server ticks between snapshots (<c>SnapshotRateDivisor</c> of <c>MatchStart</c>).</summary>
        public int SnapshotInterval { get; set; }

        public bool Started { get; private set; }

        /// <summary>The (fractional) server tick shown for the world.</summary>
        public double RenderTick { get; private set; }

        /// <summary><see cref="RenderTick"/> rounded to the nearest tick (the time of the world's tick fields).</summary>
        public int RenderTickRounded => (int)Math.Floor(RenderTick + 0.5);

        public int NewestTick { get; private set; }

        /// <summary>Arrival jitter, ticks (EMA).</summary>
        public float JitterTicks { get; private set; }

        /// <summary>Current target delay behind the newest snapshot, ticks.</summary>
        public float DelayTicks { get; private set; }

        public float Rate { get; private set; } = 1f;

        public long Jumps { get; private set; }

        public SnapshotFrame From { get; private set; }

        /// <summary>The next frame after <see cref="From"/>, or null (then the clock is at or past the newest data).</summary>
        public SnapshotFrame To { get; private set; }

        /// <summary>Fraction of the way from <see cref="From"/> to <see cref="To"/> (0 without <see cref="To"/>).</summary>
        public float Alpha { get; private set; }

        /// <summary>The render tick is past the newest frame: positions run on by velocity.</summary>
        public bool Extrapolating { get; private set; }

        /// <summary>Ticks to extrapolate from <see cref="From"/> (capped by <see cref="ClientMatchSettings.MaxExtrapolationMs"/>).</summary>
        public float ExtrapolationTicks { get; private set; }

        /// <summary>Forgets the render clock and the arrival history: the next snapshot starts it again (rejoin, long pause; S3.12).</summary>
        public void Reset()
        {
            Started = false;
            _hasArrival = false;
            _fresh = false;
            JitterTicks = 0f;
            Rate = 1f;
            From = null;
            To = null;
            Alpha = 0f;
            Extrapolating = false;
            ExtrapolationTicks = 0f;
        }

        /// <summary>A new snapshot arrived at <paramref name="arrivalMicros"/> (only newer ticks move the clock target).</summary>
        public void OnSnapshot(int tick, long arrivalMicros)
        {
            if (Started && tick <= NewestTick) return;
            if (_hasArrival && tick > _lastArrivalTick)
            {
                double expected = (tick - _lastArrivalTick) * TickMicros;
                double deviation = Math.Abs(arrivalMicros - _lastArrivalMicros - expected) / TickMicros;
                JitterTicks += (float)((deviation - JitterTicks) * _settings.InterpJitterSmoothing);
            }
            _lastArrivalMicros = arrivalMicros;
            _lastArrivalTick = tick;
            _hasArrival = true;
            _fresh = true;
            NewestTick = tick;
            DelayTicks = ComputeDelay();
            if (!Started)
            {
                RenderTick = tick - DelayTicks;
                Started = true;
            }
        }

        /// <summary>One prediction tick passed: the render clock moves about one tick towards its target.</summary>
        public void Advance()
        {
            if (!Started) return;
            double error = NewestTick - DelayTicks - RenderTick;
            bool fresh = _fresh;
            _fresh = false;
            if (error > _settings.HardResyncTicks || (fresh && -error > _settings.HardResyncTicks))
            {
                RenderTick = NewestTick - DelayTicks;
                Rate = 1f;
                Jumps++;
                return;
            }
            float dilation = (float)(error * _settings.InterpGain);
            float max = _settings.MaxTimeDilation;
            if (dilation > max) dilation = max;
            else if (dilation < -max) dilation = -max;
            Rate = 1f + dilation;
            double next = RenderTick + Rate;
            double cap = NewestTick + _settings.MaxExtrapolationMs * (SimLimits.TickRate / 1000.0);
            if (next > cap) next = cap > RenderTick ? cap : RenderTick; // hold: no data to show beyond the cap
            RenderTick = next;
        }

        /// <summary>Finds <see cref="From"/> / <see cref="To"/> around <see cref="RenderTick"/>; false without any frame.</summary>
        public bool Sample()
        {
            From = null;
            To = null;
            Alpha = 0f;
            Extrapolating = false;
            ExtrapolationTicks = 0f;
            if (!Started) return false;
            int floor = (int)Math.Floor(RenderTick);
            int oldest = NewestTick - _ring.Length + 1;
            int start = floor > NewestTick ? NewestTick : floor;
            for (int t = start; t >= oldest; t--)
            {
                SnapshotFrame f = _ring.Get(t);
                if (f == null) continue;
                From = f;
                break;
            }
            int first = floor + 1 < oldest ? oldest : floor + 1;
            for (int t = first; t <= NewestTick; t++)
            {
                SnapshotFrame f = _ring.Get(t);
                if (f == null) continue;
                To = f;
                break;
            }
            if (From == null)
            {
                // Older than everything kept (start or a jump back): show the oldest frame we have.
                From = To;
                To = null;
                return From != null;
            }
            if (To != null)
            {
                Alpha = (float)((RenderTick - From.Tick) / (To.Tick - From.Tick));
                return true;
            }
            double ahead = RenderTick - From.Tick;
            if (ahead > MinAhead)
            {
                Extrapolating = true;
                double cap = _settings.MaxExtrapolationMs * (SimLimits.TickRate / 1000.0);
                ExtrapolationTicks = (float)(ahead < cap ? ahead : cap);
            }
            return true;
        }

        private float ComputeDelay()
        {
            float delay = _settings.InterpDelaySnapshots * SnapshotInterval + _settings.InterpJitterFactor * JitterTicks;
            float max = _settings.InterpDelayMaxMs * (SimLimits.TickRate / 1000f);
            return delay > max ? max : delay;
        }
    }
}
