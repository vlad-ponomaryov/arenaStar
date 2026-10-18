using System.Diagnostics;

namespace Arena.Net.Transport
{
    /// <summary>Monotonic real time in microseconds for real sockets (<see cref="Stopwatch"/>; tests use virtual time).</summary>
    public static class NetClock
    {
        private const long MicrosPerSecond = 1000000L;

        public static long NowMicros()
        {
            long ticks = Stopwatch.GetTimestamp();
            long frequency = Stopwatch.Frequency;
            return ticks / frequency * MicrosPerSecond + ticks % frequency * MicrosPerSecond / frequency;
        }
    }
}
