using System;
using System.Net;

namespace Arena.Server.Host
{
    /// <summary>
    /// Settings of the server process itself (Server-Spec §5.3, §5.5, §5.6, §13 "S"): sockets, threads, stop, logs, metrics.
    /// The <c>"Host"</c> section of <c>server.json</c>; match, lobby and transport numbers live in <c>ServerSettings</c> and
    /// <c>TransportSettings</c>. <see cref="CreateDefault"/> holds the PROTO defaults — the single place in code for them.
    /// </summary>
    public sealed class HostSettings
    {
        /// <summary>Address of the game socket: "0.0.0.0" — every IPv4 interface, "::" — dual stack, "127.0.0.1" — this PC only.</summary>
        public string BindAddress;

        /// <summary>The single UDP port of lobby and matches (§5.3); 0 — the OS picks one (self-test).</summary>
        public int UdpPort;

        /// <summary>Mini-HTTP <c>/metrics</c> and <c>/healthz</c> (§5.6). Off — no listener.</summary>
        public bool MetricsEnabled;

        /// <summary>Metrics stay on this PC by default; on a VPS look through an SSH tunnel (§10.3).</summary>
        public string MetricsBindAddress;

        /// <summary>TCP port of the metrics; 0 — the OS picks one (self-test).</summary>
        public int MetricsPort;

        /// <summary>Game threads (§5.3: stage 3 — exactly 1; more only after the load test of S3.15).</summary>
        public int WorkerThreads;

        /// <summary>After a stop request running matches may play this long, then the process exits (§5.3, §13).</summary>
        public float DrainSeconds;

        /// <summary>After the last match ended the process keeps sending this long, so results and acknowledgements arrive.</summary>
        public float StopFlushSeconds;

        /// <summary>Lowest printed log level: Debug, Info, Warn, Error.</summary>
        public LogLevel LogLevel;

        /// <summary>One summary line every … seconds (§5.6); also the window of the tick quantiles in <c>/metrics</c>.</summary>
        public float SummaryIntervalSeconds;

        /// <summary>The game thread publishes the metrics snapshot every … seconds (§5.3).</summary>
        public float MetricsPublishSeconds;

        /// <summary>Result of each match as one JSON line (§5.6); relative paths — from the server folder. Empty — off.</summary>
        public string MatchLogPath;

        /// <summary>Maintenance at start (§5.3); the flag file switches it at run time.</summary>
        public bool Maintenance;

        /// <summary>This file next to the config turns maintenance on while it exists (§5.3). Empty — off.</summary>
        public string MaintenanceFlagFile;

        /// <summary>The flag file is checked every … seconds.</summary>
        public float MaintenanceCheckSeconds;

        /// <summary>The game thread wakes at least this often (transport timers: acks, keepalive, resends) even without ticks.</summary>
        public int MaxIdleWaitMicros;

        /// <summary>The last part of a wait before a tick deadline is spun instead of slept: sleep/poll granularity is ≈ 1 ms (§5.3).</summary>
        public int SpinWaitMicros;

        public static HostSettings CreateDefault() => new HostSettings
        {
            BindAddress = "0.0.0.0",
            UdpPort = 7777,
            MetricsEnabled = true,
            MetricsBindAddress = "127.0.0.1",
            MetricsPort = 7778,
            WorkerThreads = 1,
            DrainSeconds = 180f,
            StopFlushSeconds = 2f,
            LogLevel = LogLevel.Info,
            SummaryIntervalSeconds = 10f,
            MetricsPublishSeconds = 1f,
            MatchLogPath = "logs/matches.jsonl",
            Maintenance = false,
            MaintenanceFlagFile = "maintenance.flag",
            MaintenanceCheckSeconds = 2f,
            MaxIdleWaitMicros = 5000,
            SpinWaitMicros = 1000,
        };

        /// <summary>Throws <see cref="ArgumentException"/> if a value is out of range.</summary>
        public void Validate()
        {
            if (!IPAddress.TryParse(BindAddress ?? "", out _)) throw new ArgumentException("Host.BindAddress is not an IP address: " + BindAddress);
            if (UdpPort < 0 || UdpPort > 65535) throw new ArgumentException("Host.UdpPort must be in [0, 65535]");
            if (MetricsEnabled && !IPAddress.TryParse(MetricsBindAddress ?? "", out _))
                throw new ArgumentException("Host.MetricsBindAddress is not an IP address: " + MetricsBindAddress);
            if (MetricsPort < 0 || MetricsPort > 65535) throw new ArgumentException("Host.MetricsPort must be in [0, 65535]");
            if (WorkerThreads != 1) throw new ArgumentException("Host.WorkerThreads: stage 3 runs one game thread (Server-Spec §5.3)");
            if (DrainSeconds < 0f || StopFlushSeconds < 0f) throw new ArgumentException("Host: drain and flush times must be >= 0");
            if (SummaryIntervalSeconds <= 0f || MetricsPublishSeconds <= 0f || MaintenanceCheckSeconds <= 0f)
                throw new ArgumentException("Host: intervals must be > 0");
            if (MaxIdleWaitMicros < 100) throw new ArgumentException("Host.MaxIdleWaitMicros must be >= 100");
            if (SpinWaitMicros < 0 || SpinWaitMicros > MaxIdleWaitMicros) throw new ArgumentException("Host.SpinWaitMicros must be in [0, MaxIdleWaitMicros]");
        }

        public HostSettings Clone() => (HostSettings)MemberwiseClone();
    }
}
