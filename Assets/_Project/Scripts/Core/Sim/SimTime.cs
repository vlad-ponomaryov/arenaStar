using Arena.Core.Math;

namespace Arena.Core.Sim
{
    /// <summary>Fixed-step time of the simulation (D-003): state stores ticks (<c>int</c>), configs store seconds.</summary>
    public static class SimTime
    {
        /// <summary>Seconds per tick (1/30).</summary>
        public const float Dt = 1f / SimLimits.TickRate;

        /// <summary>Seconds → ticks, rounded to the nearest tick (1.4 s → 42). Negative input gives 0.</summary>
        public static int SecondsToTicks(float seconds) => seconds <= 0f ? 0 : MathUtil.RoundToInt(seconds * SimLimits.TickRate);

        public static float TicksToSeconds(int ticks) => ticks * Dt;
    }
}
