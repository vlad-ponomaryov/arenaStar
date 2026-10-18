using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Arena.Server.Host
{
    /// <summary>
    /// Windows timer resolution of this process (Server-Spec §5.3): without <c>timeBeginPeriod(1)</c> sleeps and socket waits
    /// last up to 15.6 ms and the 33.3 ms tick jitters. Windows 11 ignores the request of a process without a visible window
    /// (a service, a console started hidden) unless it opts out of timer power throttling
    /// (<c>PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION</c>, D-099) — done here first. Affects only this process;
    /// <see cref="Dispose"/> calls <c>timeEndPeriod(1)</c>. Elsewhere — nothing to do.
    /// </summary>
    public sealed class TimerResolution : IDisposable
    {
        private const uint PeriodMs = 1;
        private const int ProcessPowerThrottling = 4;
        private const int PowerThrottlingCurrentVersion = 1;
        private const int PowerThrottlingIgnoreTimerResolution = 0x4;
        private const int SleepSamples = 20;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint periodMs);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint periodMs);

        // PROCESS_POWER_THROTTLING_STATE { ULONG Version; ULONG ControlMask; ULONG StateMask; } passed as int[3].
        [DllImport("kernel32.dll", EntryPoint = "SetProcessInformation", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int informationClass, int[] information, int size);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
        private static extern IntPtr GetCurrentProcess();

        private bool _active;

        private TimerResolution(bool active, bool throttlingOff)
        {
            _active = active;
            ThrottlingOptOut = throttlingOff;
        }

        /// <summary>True — 1 ms resolution was set (Windows).</summary>
        public bool Active => _active;

        /// <summary>True — Windows was told to honour the resolution with no visible window.</summary>
        public bool ThrottlingOptOut { get; }

        public static TimerResolution Begin()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return new TimerResolution(false, false);
            bool optOut = false;
            try
            {
                // Control the "ignore timer resolution" throttling and turn it off (state bit 0).
                var state = new[] { PowerThrottlingCurrentVersion, PowerThrottlingIgnoreTimerResolution, 0 };
                optOut = SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, state, state.Length * sizeof(int));
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // Windows before 8: no power throttling, nothing to opt out of.
            }
            try
            {
                return new TimerResolution(TimeBeginPeriod(PeriodMs) == 0, optOut);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return new TimerResolution(false, optOut);
            }
        }

        /// <summary>Average length of <c>Thread.Sleep(1)</c> now, µs (≈ 1000–2000 with 1 ms resolution, ≈ 15600 without).</summary>
        public static long MeasureSleepMicros()
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < SleepSamples; i++) Thread.Sleep(1);
            long ticks = Stopwatch.GetTimestamp() - start;
            return ticks * 1000000L / Stopwatch.Frequency / SleepSamples;
        }

        public void Dispose()
        {
            if (!_active) return;
            _active = false;
            try
            {
                TimeEndPeriod(PeriodMs);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
            }
        }
    }
}
