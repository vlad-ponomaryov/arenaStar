using Arena.Core.Sim;

namespace Arena.Net.Client
{
    /// <summary>
    /// Display statistics of one client (Server-Spec §11.1, S3.7b) for the thresholds and the network overlay: presented ticks and
    /// those with the render clock past the data (extrapolation) with a histogram of the extrapolation runs (1 ms bins,
    /// <see cref="NetLimits.ExtrapolationHistogramBins"/>), the interpolation delay, and "teleports" — a shown fighter moving in one
    /// display tick more than <c>speed × tick × 3 + 0.3</c> cells without a spawn, dash or push (<see cref="AddMotion"/>).
    /// No allocations.
    /// </summary>
    public sealed class ClientNetStats
    {
        /// <summary>A jump this much (cells) over the allowed motion counts as a large teleport (the "bad network" threshold of §11.1).</summary>
        public const float LargeTeleportCells = 1f;

        /// <summary>Allowed motion per display tick: this many times the fighter's speed … (§11.1)</summary>
        public const float TeleportSpeedFactor = 3f;

        /// <summary>… plus this slack (cells, §11.1).</summary>
        public const float TeleportSlackCells = 0.3f;

        /// <summary>The allowed motion of a fighter with <paramref name="speed"/> (cells/s) over one display tick.</summary>
        public static float MotionLimit(float speed) => speed / SimLimits.TickRate * TeleportSpeedFactor + TeleportSlackCells;

        private const float MillisPerTick = 1000f / SimLimits.TickRate;

        private readonly long[] _runBins = new long[NetLimits.ExtrapolationHistogramBins];
        private int _runTicks;
        private double _delaySum;

        public long PresentedTicks { get; private set; }
        public long ExtrapolatedTicks { get; private set; }

        /// <summary>Finished extrapolation runs.</summary>
        public long ExtrapolationRuns { get; private set; }

        public float MaxExtrapolationMs { get; private set; }

        public long MotionSamples { get; private set; }
        public long Teleports { get; private set; }
        public long LargeTeleports { get; private set; }

        /// <summary>Largest motion over the allowed limit seen (cells).</summary>
        public float MaxTeleportExcess { get; private set; }

        /// <summary>Slot, display tick and distance of the last teleport (diagnostics).</summary>
        public int LastTeleportSlot { get; private set; } = -1;
        public int LastTeleportTick { get; private set; }
        public float LastTeleportCells { get; private set; }

        public float MeanDelayTicks => PresentedTicks > 0 ? (float)(_delaySum / PresentedTicks) : 0f;

        public double ExtrapolationShare => PresentedTicks > 0 ? ExtrapolatedTicks / (double)PresentedTicks : 0.0;

        /// <summary>One display tick built; <paramref name="extrapolating"/> — the render clock was past the newest data.</summary>
        public void AddPresentedTick(bool extrapolating, float delayTicks)
        {
            PresentedTicks++;
            _delaySum += delayTicks;
            if (extrapolating)
            {
                ExtrapolatedTicks++;
                _runTicks++;
                return;
            }
            if (_runTicks > 0) EndRun();
        }

        /// <summary>Motion of a shown fighter over one display tick against its allowed <paramref name="limit"/> (cells).</summary>
        public void AddMotion(float distance, float limit, int slot, int tick)
        {
            MotionSamples++;
            if (distance <= limit) return;
            Teleports++;
            LastTeleportSlot = slot;
            LastTeleportTick = tick;
            LastTeleportCells = distance;
            float excess = distance - limit;
            if (excess > LargeTeleportCells) LargeTeleports++;
            if (excess > MaxTeleportExcess) MaxTeleportExcess = excess;
        }

        /// <summary>Upper edge (ms) of the bin holding the <paramref name="fraction"/> quantile of extrapolation runs; 0 without runs.</summary>
        public float ExtrapolationPercentileMs(double fraction)
        {
            if (ExtrapolationRuns == 0) return 0f;
            long need = (long)System.Math.Ceiling(ExtrapolationRuns * fraction);
            if (need < 1) need = 1;
            long seen = 0;
            for (int i = 0; i < _runBins.Length; i++)
            {
                seen += _runBins[i];
                if (seen >= need) return i + 1;
            }
            return MaxExtrapolationMs;
        }

        private void EndRun()
        {
            float ms = _runTicks * MillisPerTick;
            _runTicks = 0;
            ExtrapolationRuns++;
            if (ms > MaxExtrapolationMs) MaxExtrapolationMs = ms;
            int bin = (int)ms;
            if (bin >= _runBins.Length) bin = _runBins.Length - 1;
            _runBins[bin]++;
        }
    }
}
