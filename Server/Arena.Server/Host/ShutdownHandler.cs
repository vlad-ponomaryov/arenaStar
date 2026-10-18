using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Arena.Server.Host
{
    /// <summary>
    /// Stop requests of the process (Server-Spec §5.3): SIGTERM (<see cref="PosixSignalRegistration"/> — Docker, systemd) and
    /// Ctrl+C (<see cref="Console.CancelKeyPress"/>) are a thin wrapper over <see cref="RequestStop"/>; the self-test calls the
    /// same method. The first request starts the drain (no new matches, running ones play to the end within <c>DrainSeconds</c>);
    /// a second one forces an immediate stop. Thread-safe: signals arrive on other threads, the game thread polls the flags.
    /// </summary>
    public sealed class ShutdownHandler : IDisposable
    {
        private int _requests;
        private PosixSignalRegistration _sigterm;
        private PosixSignalRegistration _sigint;
        private ConsoleCancelEventHandler _cancel;

        /// <summary>A drain was requested.</summary>
        public bool StopRequested => Volatile.Read(ref _requests) > 0;

        /// <summary>A second request came: stop now, without waiting for matches.</summary>
        public bool ForceRequested => Volatile.Read(ref _requests) > 1;

        /// <summary>Signal name of the first request ("SIGTERM", "Ctrl+C", "self-test" …), for the log.</summary>
        public string Source { get; private set; }

        /// <summary>Hooks SIGTERM, SIGINT and Ctrl+C of this process (the self-test does not call it).</summary>
        public void InstallSignalHandlers()
        {
            _cancel = (sender, e) =>
            {
                e.Cancel = true; // keep running: the drain decides when the process exits
                RequestStop("Ctrl+C");
            };
            Console.CancelKeyPress += _cancel;
            try
            {
                _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c =>
                {
                    c.Cancel = true;
                    RequestStop("SIGTERM");
                });
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c =>
                    {
                        c.Cancel = true;
                        RequestStop("SIGINT");
                    });
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Ctrl+C still works; SIGTERM falls back to the default (immediate) exit.
            }
        }

        /// <summary>Asks the server to stop (drain); a second call — stop at once.</summary>
        public void RequestStop(string source)
        {
            if (Interlocked.Increment(ref _requests) == 1) Source = source ?? "request";
        }

        public void Dispose()
        {
            if (_cancel != null) Console.CancelKeyPress -= _cancel;
            _cancel = null;
            _sigterm?.Dispose();
            _sigint?.Dispose();
            _sigterm = null;
            _sigint = null;
        }
    }
}
