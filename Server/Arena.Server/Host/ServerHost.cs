using System;
using System.IO;
using System.Net;
using System.Threading;
using Arena.Net.Server.Lobby;
using Arena.Net.Server.Transport;
using Arena.Net.Transport;

namespace Arena.Server.Host
{
    /// <summary>
    /// The real server process (Server-Spec §5.3): one UDP socket, one <see cref="ServerNode"/> (lobby + matches), one game
    /// thread. A pass: receive → lobby → due match steps → transport timers and flushes (<see cref="ServerNode.Update"/>); then
    /// the thread sleeps on the socket (<c>Socket.Poll</c>, wakes on a packet) until the earliest room deadline or
    /// <c>MaxIdleWaitMicros</c>, spinning the last <c>SpinWaitMicros</c> so the deadline is hit to ≈ 0.1 ms (Windows: with
    /// <see cref="TimerResolution"/>). Deadlines are counted from the room's start (no drift); lateness and work time of every
    /// due pass go into histograms. Once a second the counters are published to the <see cref="MetricsEndpoint"/>, every
    /// <c>SummaryIntervalSeconds</c> one summary line is logged, every <c>MaintenanceCheckSeconds</c> the maintenance flag file is
    /// checked. A stop request (<see cref="ShutdownHandler"/>) starts the drain: <see cref="ServerNode.BeginShutdown"/>, running
    /// matches play to the end (at most <c>DrainSeconds</c>), results are flushed for <c>StopFlushSeconds</c>, then
    /// <see cref="Run"/> returns. The pass itself allocates nothing (only the bootstrap socket does, per packet, §4.6).
    /// </summary>
    public sealed class ServerHost : IDisposable
    {
        private const long MicrosPerSecond = 1000000L;
        private const int SpinIterations = 20;

        private readonly ServerConfig _config;
        private readonly HostSettings _host;
        private readonly ServerContent _content;
        private readonly ConsoleLog _log;
        private readonly ShutdownHandler _shutdown;
        private readonly INetRandom _random;
        private readonly MetricsSnapshot _snapshot = new MetricsSnapshot();
        private readonly LatencyHistogram _windowWork = new LatencyHistogram();
        private readonly LatencyHistogram _windowLateness = new LatencyHistogram();

        private IWaitableServerSocket _socket;
        private MetricsEndpoint _metrics;
        private MatchResultLog _results;
        private Thread _thread;
        private long _startMicros;
        private long _prevPacketsIn, _prevPacketsOut, _prevBytesIn, _prevBytesOut, _prevTicks;

        public ServerHost(ServerConfig config, ServerContent content, ConsoleLog log, ShutdownHandler shutdown, INetRandom random = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.Validate();
            _host = config.Host;
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
            _random = random ?? new CryptoNetRandom();
        }

        public ServerNode Node { get; private set; }

        /// <summary>Bound game socket (after <see cref="Start"/>).</summary>
        public IPEndPoint UdpEndPoint { get; private set; }

        /// <summary>Bound metrics endpoint, null — metrics off.</summary>
        public IPEndPoint MetricsEndPoint => _metrics?.LocalEndPoint;

        public MatchResultLog Results => _results;

        /// <summary>Lateness of due passes against their deadline since start (µs) — the tick jitter of §5.3.</summary>
        public LatencyHistogram TickLateness { get; } = new LatencyHistogram();

        /// <summary>Work time of due passes since start (µs).</summary>
        public LatencyHistogram TickWork { get; } = new LatencyHistogram();

        public long Updates { get; private set; }

        public bool TimerResolutionActive { get; private set; }

        /// <summary>Measured <c>Thread.Sleep(1)</c> at start, µs (diagnostics of the wait, §5.3).</summary>
        public long SleepMicros { get; private set; }

        /// <summary>Exception that ended <see cref="RunOnThread"/>'s thread, null — none.</summary>
        public Exception Failure { get; private set; }

        /// <summary>How the loop ended: "drained", "drain timeout", "forced".</summary>
        public string StopReason { get; private set; }

        /// <summary>Binds the socket and the metrics port, creates the node. Throws on a busy port or a bad address.</summary>
        public void Start()
        {
            if (Node != null) throw new InvalidOperationException("ServerHost: already started");
            var local = new IPEndPoint(IPAddress.Parse(_host.BindAddress), _host.UdpPort);
            _socket = ServerSocketFactory.Create(local, out IPEndPoint bound);
            UdpEndPoint = bound;
            Node = new ServerNode(_socket, _config.Transport, _config.Server, _content.Bundle, _content.Hash, _random);
            string resultPath = string.IsNullOrEmpty(_host.MatchLogPath) ? null : ResolvePath(AppContext.BaseDirectory, _host.MatchLogPath);
            _results = new MatchResultLog(resultPath, Node.Transport, _log);
            Node.Matches.ResultSink = _results;
            if (_host.MetricsEnabled)
            {
                try
                {
                    _metrics = new MetricsEndpoint(new IPEndPoint(IPAddress.Parse(_host.MetricsBindAddress), _host.MetricsPort), _log);
                }
                catch (System.Net.Sockets.SocketException e)
                {
                    _log.Error("metrics port " + _host.MetricsPort + " unavailable (" + e.SocketErrorCode + "), metrics off");
                }
            }
            _log.Info("listening on udp " + bound + " (" + ServerSocketFactory.Kind + "), metrics "
                + (_metrics == null ? "off" : "http://" + _metrics.LocalEndPoint + "/metrics") + ", results "
                + (resultPath ?? "off") + ", max matches " + _config.Server.MaxMatches + ", lan discovery " + _config.Transport.LanDiscovery);
        }

        /// <summary>Runs <see cref="Run"/> on a new game thread (self-test); the exception, if any, goes to <see cref="Failure"/>.</summary>
        public void RunOnThread()
        {
            _thread = new Thread(() =>
            {
                try
                {
                    Run();
                }
                catch (Exception e)
                {
                    Failure = e;
                    _log.Error("game thread crashed: " + e);
                }
            }) { IsBackground = true, Name = "arena-game" };
            _thread.Start();
        }

        public bool Join(int timeoutMs) => _thread == null || _thread.Join(timeoutMs);

        /// <summary>The game loop on the calling thread until the drain is over.</summary>
        public void Run()
        {
            if (Node == null) Start();
            using (TimerResolution timer = TimerResolution.Begin())
            {
                TimerResolutionActive = timer.Active;
                SleepMicros = TimerResolution.MeasureSleepMicros();
                _log.Info("timer: 1 ms resolution " + (timer.Active ? "on" : "n/a") + ", throttling opt-out " + (timer.ThrottlingOptOut ? "on" : "n/a")
                    + ", Sleep(1) = " + Ms(SleepMicros) + " ms");
                Loop();
            }
            Publish(NetClock.NowMicros());
        }

        private void Loop()
        {
            _startMicros = NetClock.NowMicros();
            long publishEvery = Seconds(_host.MetricsPublishSeconds);
            long summaryEvery = Seconds(_host.SummaryIntervalSeconds);
            long maintenanceEvery = Seconds(_host.MaintenanceCheckSeconds);
            long nextPublish = _startMicros;
            long nextSummary = _startMicros + summaryEvery;
            long nextMaintenance = _startMicros;
            long due = long.MaxValue;
            bool draining = false;
            long drainDeadline = 0, flushUntil = 0;
            while (true)
            {
                long now = NetClock.NowMicros();
                bool stepDue = due != long.MaxValue && now >= due;
                if (stepDue)
                {
                    TickLateness.Record(now - due);
                    _windowLateness.Record(now - due);
                }
                Node.Update(now);
                Updates++;
                long after = NetClock.NowMicros();
                if (stepDue)
                {
                    TickWork.Record(after - now);
                    _windowWork.Record(after - now);
                }

                if (!draining && _shutdown.StopRequested)
                {
                    draining = true;
                    drainDeadline = after + Seconds(_host.DrainSeconds);
                    Node.BeginShutdown((int)Math.Ceiling(_host.DrainSeconds));
                    _log.Info("stop requested (" + _shutdown.Source + "): no new matches, " + Node.Matches.PlayingMatches
                        + " running match(es) play on, at most " + _host.DrainSeconds + " s");
                }
                if (draining)
                {
                    if (_shutdown.ForceRequested)
                    {
                        StopReason = "forced";
                        _log.Warn("second stop request: exiting now, " + Node.Matches.PlayingMatches + " match(es) cut off");
                        return;
                    }
                    if (flushUntil == 0 && (Node.Matches.PlayingMatches == 0 || after >= drainDeadline))
                    {
                        StopReason = Node.Matches.PlayingMatches == 0 ? "drained" : "drain timeout";
                        flushUntil = after + Seconds(_host.StopFlushSeconds);
                        _log.Info("drain over (" + StopReason + "), flushing " + _host.StopFlushSeconds + " s");
                    }
                    if (flushUntil != 0 && after >= flushUntil) return;
                }
                if (after >= nextMaintenance)
                {
                    nextMaintenance = after + maintenanceEvery;
                    CheckMaintenance();
                }
                if (after >= nextPublish)
                {
                    nextPublish = after + publishEvery;
                    Publish(after);
                }
                if (after >= nextSummary)
                {
                    LogSummary(after, summaryEvery);
                    nextSummary = after + summaryEvery;
                }

                due = Node.Matches.NextStepDueMicros();
                long wake = after + _host.MaxIdleWaitMicros;
                if (due < wake) wake = due;
                Wait(wake);
            }
        }

        private void Wait(long wakeMicros)
        {
            while (true)
            {
                long remaining = wakeMicros - NetClock.NowMicros();
                if (remaining <= 0) return;
                if (remaining > _host.SpinWaitMicros)
                {
                    // Socket.Poll with a timeout sleeps in steps of the system timer on Windows (up to 15.6 ms even with
                    // timeBeginPeriod, measured by the self-test, D-099); Sleep(1) honours the 1 ms resolution. A pending packet
                    // ends the wait at once.
                    if (_socket.WaitReadable(0)) return;
                    Thread.Sleep(1);
                    continue;
                }
                Thread.SpinWait(SpinIterations);
            }
        }

        private void CheckMaintenance()
        {
            bool flag = false;
            if (!string.IsNullOrEmpty(_host.MaintenanceFlagFile))
            {
                string dir = _config.Directory ?? AppContext.BaseDirectory;
                flag = File.Exists(ResolvePath(dir, _host.MaintenanceFlagFile));
            }
            bool on = _host.Maintenance || flag;
            if (on == Node.Maintenance || Node.ShuttingDown) return;
            Node.SetMaintenance(on);
            _log.Info(on ? "maintenance ON: new matches refused, running matches play on" : "maintenance OFF");
        }

        private void Publish(long nowMicros)
        {
            if (_metrics == null) return;
            _snapshot.Capture(Node, _socket, _windowWork, _windowLateness, (nowMicros - _startMicros) / (double)MicrosPerSecond, Updates);
            _metrics.Publish(_snapshot);
        }

        private void LogSummary(long nowMicros, long windowMicros)
        {
            ServerTransportCounters t = Node.Transport.Counters;
            double seconds = windowMicros / (double)MicrosPerSecond;
            long ticks = Node.Matches.Metrics.Ticks;
            _log.Info("summary: matches " + Node.Matches.Metrics.ActiveMatches + " (playing " + Node.Matches.PlayingMatches + "), players "
                + Node.Matches.Metrics.ConnectedPlayers + ", lobby " + Node.LobbyMetrics.LobbyConnections + ", queue "
                + Node.LobbyMetrics.QueuedPlayers + ", sim ticks/s " + Rate(ticks - _prevTicks, seconds)
                + ", tick work p99 " + Ms(_windowWork.PercentileMicros(0.99)) + " max " + Ms(_windowWork.MaxMicros)
                + " ms, lateness p99 " + Ms(_windowLateness.PercentileMicros(0.99)) + " max " + Ms(_windowLateness.MaxMicros)
                + " ms, in " + Rate(t.PacketsReceived - _prevPacketsIn, seconds) + " pkt/s " + Rate((t.BytesReceived - _prevBytesIn) / 1024.0, seconds)
                + " KB/s, out " + Rate(t.PacketsSent - _prevPacketsOut, seconds) + " pkt/s " + Rate((t.BytesSent - _prevBytesOut) / 1024.0, seconds)
                + " KB/s, skipped ticks " + Node.Matches.Metrics.SkippedTicks + (Node.Maintenance ? ", MAINTENANCE" : "")
                + (Node.ShuttingDown ? ", DRAINING" : ""));
            _prevTicks = ticks;
            _prevPacketsIn = t.PacketsReceived;
            _prevPacketsOut = t.PacketsSent;
            _prevBytesIn = t.BytesReceived;
            _prevBytesOut = t.BytesSent;
            _windowWork.Reset();
            _windowLateness.Reset();
        }

        private static string Rate(double value, double seconds) => MetricsSnapshot.Format(Math.Round(value / seconds, 1));

        private static string Ms(long micros) => MetricsSnapshot.Format(Math.Round(micros / 1000.0, 3));

        private static long Seconds(float seconds) => (long)(seconds * MicrosPerSecond);

        private static string ResolvePath(string baseDir, string path) => Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);

        public void Dispose()
        {
            _metrics?.Dispose();
            _metrics = null;
            _socket?.Close();
        }
    }
}
