using System;
using System.Globalization;
using System.Text;
using Arena.Net.Server.Lobby;
using Arena.Net.Server.Match;
using Arena.Net.Transport;

namespace Arena.Server.Host
{
    /// <summary>
    /// One published copy of the server counters (Server-Spec §5.3, §5.6): the game thread fills its own instance with
    /// <see cref="Capture"/> once a second (no allocations) and copies it under a lock into the shared one; the HTTP thread copies
    /// the shared one out under the same lock and formats it with <see cref="WritePrometheus"/> — it never touches live objects.
    /// Seconds and shares are printed with a dot (invariant culture).
    /// </summary>
    public sealed class MetricsSnapshot
    {
        public double UptimeSeconds;
        public bool Maintenance;
        public bool Draining;

        public int ActiveMatches;
        public int PlayingMatches;
        public long MatchesCreated;
        public long MatchesEnded;
        public int ConnectedPlayers;
        public int Connections;

        public int LobbyConnections;
        public int QueuedPlayers;
        public long QueueJoins;
        public long MatchesAssembled;
        public long BotFilledMatches;
        public long FillBots;
        public double QueueMaxWaitSeconds;

        public long SimTicks;
        public long SkippedTicks;
        public long Updates;
        public double TickWorkP50Seconds;
        public double TickWorkP99Seconds;
        public double TickWorkMaxSeconds;
        public double TickLatenessP50Seconds;
        public double TickLatenessP99Seconds;
        public double TickLatenessMaxSeconds;

        public long PacketsReceived;
        public long PacketsSent;
        public long BytesReceived;
        public long BytesSent;
        public long SnapshotsSent;
        public double SnapshotAverageBytes;
        public int SnapshotMaxBytes;
        public long Keyframes;

        public long LateInputs;
        public double MissingInputShare;
        public long PressesCarried;
        public long Takeovers;
        public long Leaves;

        public long RejectedConnections;
        public long RateLimited;
        public long Timeouts;
        public long Malformed;
        public long SocketResets;

        public long GcHeapBytes;
        public long GcGen0;
        public long GcGen2;

        /// <summary>Fills from the live objects (game thread only); the tick quantiles come from the window histograms.</summary>
        public void Capture(ServerNode node, IServerDatagramSocket socket, LatencyHistogram tickWork, LatencyHistogram tickLateness,
            double uptimeSeconds, long updates)
        {
            ServerMetrics m = node.Matches.Metrics;
            LobbyMetrics l = node.LobbyMetrics;
            ServerTransportCounters t = node.Transport.Counters;
            UptimeSeconds = uptimeSeconds;
            Maintenance = node.Maintenance;
            Draining = node.ShuttingDown;
            ActiveMatches = m.ActiveMatches;
            PlayingMatches = node.Matches.PlayingMatches;
            MatchesCreated = m.MatchesCreated;
            MatchesEnded = m.MatchesEnded;
            ConnectedPlayers = m.ConnectedPlayers;
            Connections = node.Transport.Connections.ActiveCount;
            LobbyConnections = l.LobbyConnections;
            QueuedPlayers = l.QueuedPlayers;
            QueueJoins = l.Joins;
            MatchesAssembled = l.MatchesAssembled;
            BotFilledMatches = l.BotFilledMatches;
            FillBots = l.FillBots;
            QueueMaxWaitSeconds = l.MaxWaitMicros / 1e6;
            SimTicks = m.Ticks;
            SkippedTicks = m.SkippedTicks;
            Updates = updates;
            TickWorkP50Seconds = tickWork.PercentileMicros(0.5) / 1e6;
            TickWorkP99Seconds = tickWork.PercentileMicros(0.99) / 1e6;
            TickWorkMaxSeconds = tickWork.MaxMicros / 1e6;
            TickLatenessP50Seconds = tickLateness.PercentileMicros(0.5) / 1e6;
            TickLatenessP99Seconds = tickLateness.PercentileMicros(0.99) / 1e6;
            TickLatenessMaxSeconds = tickLateness.MaxMicros / 1e6;
            PacketsReceived = t.PacketsReceived;
            PacketsSent = t.PacketsSent;
            BytesReceived = t.BytesReceived;
            BytesSent = t.BytesSent;
            SnapshotsSent = m.SnapshotsSent;
            SnapshotAverageBytes = m.AverageSnapshotBytes;
            SnapshotMaxBytes = m.MaxSnapshotBytes;
            Keyframes = m.Keyframes;
            LateInputs = m.LateInputs;
            MissingInputShare = m.MissingInputShare;
            PressesCarried = m.PressesCarried;
            Takeovers = m.Takeovers;
            Leaves = m.Leaves;
            RejectedConnections = t.Rejected + m.RejectedConnections + l.RejectedJoins;
            RateLimited = t.DroppedRateLimited + t.RateLimitDisconnects + node.Transport.RateLimitedHandshakes;
            Timeouts = t.Timeouts;
            Malformed = t.DroppedMalformed + m.MalformedMessages + l.MalformedMessages;
            SocketResets = socket == null ? 0 : socket.ResetCount;
            GcHeapBytes = GC.GetTotalMemory(false);
            GcGen0 = GC.CollectionCount(0);
            GcGen2 = GC.CollectionCount(2);
        }

        public void CopyFrom(MetricsSnapshot other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            // All fields are value types: a field-wise copy without allocations.
            UptimeSeconds = other.UptimeSeconds; Maintenance = other.Maintenance; Draining = other.Draining;
            ActiveMatches = other.ActiveMatches; PlayingMatches = other.PlayingMatches; MatchesCreated = other.MatchesCreated;
            MatchesEnded = other.MatchesEnded; ConnectedPlayers = other.ConnectedPlayers; Connections = other.Connections;
            LobbyConnections = other.LobbyConnections; QueuedPlayers = other.QueuedPlayers; QueueJoins = other.QueueJoins;
            MatchesAssembled = other.MatchesAssembled; BotFilledMatches = other.BotFilledMatches; FillBots = other.FillBots;
            QueueMaxWaitSeconds = other.QueueMaxWaitSeconds;
            SimTicks = other.SimTicks; SkippedTicks = other.SkippedTicks; Updates = other.Updates;
            TickWorkP50Seconds = other.TickWorkP50Seconds; TickWorkP99Seconds = other.TickWorkP99Seconds; TickWorkMaxSeconds = other.TickWorkMaxSeconds;
            TickLatenessP50Seconds = other.TickLatenessP50Seconds; TickLatenessP99Seconds = other.TickLatenessP99Seconds;
            TickLatenessMaxSeconds = other.TickLatenessMaxSeconds;
            PacketsReceived = other.PacketsReceived; PacketsSent = other.PacketsSent; BytesReceived = other.BytesReceived;
            BytesSent = other.BytesSent; SnapshotsSent = other.SnapshotsSent; SnapshotAverageBytes = other.SnapshotAverageBytes;
            SnapshotMaxBytes = other.SnapshotMaxBytes; Keyframes = other.Keyframes;
            LateInputs = other.LateInputs; MissingInputShare = other.MissingInputShare; PressesCarried = other.PressesCarried;
            Takeovers = other.Takeovers; Leaves = other.Leaves;
            RejectedConnections = other.RejectedConnections; RateLimited = other.RateLimited; Timeouts = other.Timeouts;
            Malformed = other.Malformed; SocketResets = other.SocketResets;
            GcHeapBytes = other.GcHeapBytes; GcGen0 = other.GcGen0; GcGen2 = other.GcGen2;
        }

        /// <summary>Prometheus text format 0.0.4 (HTTP thread; allocates).</summary>
        public void WritePrometheus(StringBuilder sb)
        {
            Gauge(sb, "arena_up", 1);
            Gauge(sb, "arena_uptime_seconds", UptimeSeconds);
            Gauge(sb, "arena_maintenance", Maintenance ? 1 : 0);
            Gauge(sb, "arena_draining", Draining ? 1 : 0);
            Gauge(sb, "arena_matches_active", ActiveMatches);
            Gauge(sb, "arena_matches_playing", PlayingMatches);
            Counter(sb, "arena_matches_created_total", MatchesCreated);
            Counter(sb, "arena_matches_ended_total", MatchesEnded);
            Gauge(sb, "arena_players_connected", ConnectedPlayers);
            Gauge(sb, "arena_connections", Connections);
            Gauge(sb, "arena_lobby_connections", LobbyConnections);
            Gauge(sb, "arena_queue_players", QueuedPlayers);
            Counter(sb, "arena_queue_joins_total", QueueJoins);
            Counter(sb, "arena_lobby_matches_assembled_total", MatchesAssembled);
            Counter(sb, "arena_lobby_bot_filled_matches_total", BotFilledMatches);
            Counter(sb, "arena_lobby_fill_bots_total", FillBots);
            Gauge(sb, "arena_queue_max_wait_seconds", QueueMaxWaitSeconds);
            Counter(sb, "arena_sim_ticks_total", SimTicks);
            Counter(sb, "arena_skipped_ticks_total", SkippedTicks);
            Counter(sb, "arena_updates_total", Updates);
            Quantiles(sb, "arena_tick_work_seconds", TickWorkP50Seconds, TickWorkP99Seconds, TickWorkMaxSeconds);
            Quantiles(sb, "arena_tick_lateness_seconds", TickLatenessP50Seconds, TickLatenessP99Seconds, TickLatenessMaxSeconds);
            Counter(sb, "arena_packets_received_total", PacketsReceived);
            Counter(sb, "arena_packets_sent_total", PacketsSent);
            Counter(sb, "arena_bytes_received_total", BytesReceived);
            Counter(sb, "arena_bytes_sent_total", BytesSent);
            Counter(sb, "arena_snapshots_sent_total", SnapshotsSent);
            Gauge(sb, "arena_snapshot_average_bytes", SnapshotAverageBytes);
            Gauge(sb, "arena_snapshot_max_bytes", SnapshotMaxBytes);
            Counter(sb, "arena_keyframes_total", Keyframes);
            Counter(sb, "arena_late_inputs_total", LateInputs);
            Gauge(sb, "arena_missing_input_share", MissingInputShare);
            Counter(sb, "arena_presses_carried_total", PressesCarried);
            Counter(sb, "arena_takeovers_total", Takeovers);
            Counter(sb, "arena_leaves_total", Leaves);
            Counter(sb, "arena_rejected_total", RejectedConnections);
            Counter(sb, "arena_rate_limited_total", RateLimited);
            Counter(sb, "arena_timeouts_total", Timeouts);
            Counter(sb, "arena_malformed_total", Malformed);
            Counter(sb, "arena_socket_resets_total", SocketResets);
            Gauge(sb, "arena_gc_heap_bytes", GcHeapBytes);
            Counter(sb, "arena_gc_gen0_total", GcGen0);
            Counter(sb, "arena_gc_gen2_total", GcGen2);
        }

        private static void Gauge(StringBuilder sb, string name, double value) => Line(sb, name, "gauge", value);

        private static void Counter(StringBuilder sb, string name, double value) => Line(sb, name, "counter", value);

        private static void Line(StringBuilder sb, string name, string type, double value)
        {
            sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
            sb.Append(name).Append(' ').Append(Format(value)).Append('\n');
        }

        private static void Quantiles(StringBuilder sb, string name, double p50, double p99, double max)
        {
            sb.Append("# TYPE ").Append(name).Append(" gauge\n");
            sb.Append(name).Append("{quantile=\"0.5\"} ").Append(Format(p50)).Append('\n');
            sb.Append(name).Append("{quantile=\"0.99\"} ").Append(Format(p99)).Append('\n');
            sb.Append("# TYPE ").Append(name).Append("_max gauge\n");
            sb.Append(name).Append("_max ").Append(Format(max)).Append('\n');
        }

        /// <summary>A number the same way on every PC ("0.000123", never "0,000123").</summary>
        public static string Format(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    }
}
