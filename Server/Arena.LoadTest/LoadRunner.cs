using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using Arena.Net.Transport;
using Arena.Server;
using Arena.Server.Host;

namespace Arena.LoadTest
{
    /// <summary>
    /// The load test (Server-Spec §11.2, S3.15): starts the real server as a child process on 127.0.0.1 with a generated
    /// config, ramps up <c>matches × players</c> light clients (<see cref="LoadClient"/>, spread over a few client threads) that
    /// search the mode through the lobby and play, waits until every match runs, warms up, then measures for
    /// <c>--seconds</c>: the server's CPU (process time / wall time, in cores), memory (working set, private, GC heap), tick
    /// work and lateness (max over the 1-s polls of <c>/metrics</c>' window p99), skipped ticks, traffic per match and the loss
    /// on loopback (packets the server sent vs packets the clients got, and back). Prints a report; the last line is
    /// <c>LOADTEST: OK …</c> or <c>LOADTEST: FAILED …</c>.
    /// </summary>
    public sealed class LoadRunner
    {
        private const long MicrosPerSecond = 1000000L;
        private const int ClientPassSleepMs = 1;
        private const int HealthWaitMs = 20000;
        private const int PollMs = 1000;
        private const int FreshPollMs = 10;
        private const int FreshTimeoutMs = 3000;
        private const float ConfigSummarySeconds = 5f;
        private const int ConnectionsSlack = 100;
        private const int HandshakesPerClient = 4;
        private const int MatchesSlack = 10;

        private readonly LoadTestOptions _o;
        private readonly TextWriter _out;
        private LoadClient[] _clients;
        private volatile bool _stop;

        public LoadRunner(LoadTestOptions options, TextWriter output)
        {
            _o = options;
            _out = output;
        }

        public bool Run()
        {
            string baseDir = AppContext.BaseDirectory;
            string serverDll = _o.ServerDll ?? Path.Combine(baseDir, "Arena.Server.dll");
            if (!File.Exists(serverDll)) return Fail("no server dll " + serverDll);
            ServerContent content = ServerContent.Load(ServerContent.DefaultPath);
            string dir = Path.Combine(baseDir, "loadtest");
            Directory.CreateDirectory(dir);
            ServerConfig config = CreateConfig(dir);
            string configPath = Path.Combine(dir, "server.json");
            File.WriteAllText(configPath, ServerSettingsLoader.ToJson(config));
            string dotnet = Process.GetCurrentProcess().MainModule.FileName;
            int total = _o.Matches * _o.PlayersPerMatch;
            _out.WriteLine("load test: " + _o.Matches + " " + _o.Mode + " matches, " + total + " clients on " + _o.ClientThreads
                + " client threads, measure " + Num(_o.MeasureSeconds) + " s; server " + serverDll + " (" + Program.VersionLine(content) + ")");

            using (var server = new ServerProcess(dotnet, serverDll, configPath, Path.Combine(dir, "server.log")))
            {
                var metricsEndPoint = new IPEndPoint(IPAddress.Loopback, _o.MetricsPort);
                var udp = new IPEndPoint(IPAddress.Loopback, _o.Port);
                if (!WaitHealthy(server, metricsEndPoint)) return Fail("the server did not start (exit " + server.ExitCode + "), see " + Path.Combine(dir, "server.log"));

                _clients = new LoadClient[total];
                for (int i = 0; i < total; i++)
                {
                    _clients[i] = new LoadClient(config.Transport, content.Hash, udp, i * 7 + 1)
                    {
                        Mode = _o.Mode,
                        FighterIndex = i % content.Bundle.Fighters.Length
                    };
                }
                var threads = new Thread[_o.ClientThreads];
                for (int t = 0; t < threads.Length; t++)
                {
                    int index = t;
                    threads[t] = new Thread(() => ClientLoop(index)) { IsBackground = true, Name = "load-clients-" + t };
                    threads[t].Start();
                }
                try
                {
                    return Measure(server, metricsEndPoint, total);
                }
                finally
                {
                    _stop = true;
                    foreach (Thread t in threads) t.Join(5000);
                    foreach (LoadClient c in _clients)
                    {
                        c.Net.Disconnect();
                        c.Socket.Close();
                    }
                }
            }
        }

        private ServerConfig CreateConfig(string dir)
        {
            ServerConfig c = ServerConfig.CreateDefault();
            c.Host.BindAddress = "127.0.0.1";
            c.Host.UdpPort = _o.Port;
            c.Host.MetricsBindAddress = "127.0.0.1";
            c.Host.MetricsPort = _o.MetricsPort;
            c.Host.MatchLogPath = Path.Combine(dir, "matches.jsonl");
            c.Host.MaintenanceFlagFile = "";
            c.Host.SummaryIntervalSeconds = ConfigSummarySeconds;
            int clients = _o.Matches * _o.PlayersPerMatch;
            c.Server.MaxMatches = Math.Max(c.Server.MaxMatches, _o.Matches + MatchesSlack);
            c.Transport.LanDiscovery = false;
            c.Transport.MaxConnections = Math.Max(c.Transport.MaxConnections, clients + ConnectionsSlack);
            // Every client comes from 127.0.0.1: the per-IP handshake limit (§3.3) would refuse the ramp.
            c.Transport.HandshakesPerIpPerMinute = Math.Max(c.Transport.HandshakesPerIpPerMinute, clients * HandshakesPerClient);
            return c;
        }

        private bool WaitHealthy(ServerProcess server, IPEndPoint metrics)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < HealthWaitMs)
            {
                if (server.HasExited) return false;
                if (MetricsReader.Healthy(metrics)) return true;
                Thread.Sleep(200);
            }
            return false;
        }

        private void ClientLoop(int index)
        {
            int n = _o.ClientThreads;
            while (!_stop)
            {
                long now = NetClock.NowMicros();
                for (int i = index; i < _clients.Length; i += n) _clients[i].Update(now);
                Thread.Sleep(ClientPassSleepMs);
            }
        }

        private bool Measure(ServerProcess server, IPEndPoint metrics, int total)
        {
            // Ramp: RampClientsPerSecond searches per second.
            var sw = Stopwatch.StartNew();
            int started = 0;
            while (started < total)
            {
                int due = (int)Math.Min(total, (long)(sw.Elapsed.TotalSeconds * _o.RampClientsPerSecond) + 1);
                for (; started < due; started++) _clients[started].SearchWanted = true;
                Thread.Sleep(10);
            }
            int running = 0;
            while (sw.Elapsed.TotalSeconds < _o.StartTimeoutSeconds)
            {
                if (server.HasExited) return Fail("the server exited (code " + server.ExitCode + ") during the ramp");
                running = CountRunning();
                if (running == total) break;
                Thread.Sleep(250);
            }
            double rampSeconds = sw.Elapsed.TotalSeconds;
            _out.WriteLine("ramp: " + running + " of " + total + " clients in a running match after " + Num(rampSeconds) + " s");
            if (running < total) return Fail(running + " of " + total + " clients running after " + Num(_o.StartTimeoutSeconds) + " s: " + Describe());
            Thread.Sleep((int)(_o.WarmupSeconds * 1000));

            // The server publishes its counters once a second: take the client totals right after a fresh publish, so that
            // both sides of the loss count cover the same time (within FreshPollMs).
            Dictionary<string, double> a = FetchFresh(metrics);
            if (a == null) return Fail("/metrics unavailable");
            LoadTotals ca = ClientTotals();
            TimeSpan cpuA = server.CpuTime;
            TimeSpan selfCpuA = Process.GetCurrentProcess().TotalProcessorTime;
            var window = Stopwatch.StartNew();
            double maxWorkP99 = 0, maxLateP99 = 0, maxWork = 0, maxLate = 0, minPlaying = double.MaxValue;
            long peakWorking = 0, peakPrivate = 0;
            double peakHeap = 0;
            while (window.Elapsed.TotalSeconds < _o.MeasureSeconds)
            {
                Thread.Sleep(PollMs);
                if (server.HasExited) return Fail("the server exited (code " + server.ExitCode + ") under load");
                Dictionary<string, double> m = MetricsReader.Fetch(metrics);
                if (m == null) continue;
                maxWorkP99 = Math.Max(maxWorkP99, MetricsReader.Get(m, "arena_tick_work_seconds{quantile=\"0.99\"}"));
                maxLateP99 = Math.Max(maxLateP99, MetricsReader.Get(m, "arena_tick_lateness_seconds{quantile=\"0.99\"}"));
                maxWork = Math.Max(maxWork, MetricsReader.Get(m, "arena_tick_work_seconds_max"));
                maxLate = Math.Max(maxLate, MetricsReader.Get(m, "arena_tick_lateness_seconds_max"));
                minPlaying = Math.Min(minPlaying, MetricsReader.Get(m, "arena_matches_playing"));
                peakHeap = Math.Max(peakHeap, MetricsReader.Get(m, "arena_gc_heap_bytes"));
                peakWorking = Math.Max(peakWorking, server.WorkingSetBytes);
                peakPrivate = Math.Max(peakPrivate, server.PrivateBytes);
            }
            double seconds = window.Elapsed.TotalSeconds;
            Dictionary<string, double> b = FetchFresh(metrics);
            if (b == null) return Fail("/metrics unavailable at the end");
            LoadTotals cb = ClientTotals();
            TimeSpan cpuB = server.CpuTime;
            TimeSpan selfCpuB = Process.GetCurrentProcess().TotalProcessorTime;

            double cores = (cpuB - cpuA).TotalSeconds / seconds;
            double clientCores = (selfCpuB - selfCpuA).TotalSeconds / seconds;
            double simTicks = Delta(a, b, "arena_sim_ticks_total");
            double skipped = Delta(a, b, "arena_skipped_ticks_total");
            double sentPackets = Delta(a, b, "arena_packets_sent_total");
            double recvPackets = Delta(a, b, "arena_packets_received_total");
            double sentBytes = Delta(a, b, "arena_bytes_sent_total");
            double recvBytes = Delta(a, b, "arena_bytes_received_total");
            double gen0 = Delta(a, b, "arena_gc_gen0_total");
            double gen2 = Delta(a, b, "arena_gc_gen2_total");
            double lateInputs = Delta(a, b, "arena_late_inputs_total");
            // Both sides are read within FreshPollMs of each other: a small negative difference is that skew, shown as 0.
            double downLoss = sentPackets <= 0 ? 0 : Math.Max(0.0, 1.0 - (cb.PacketsReceived - ca.PacketsReceived) / sentPackets);
            double upLoss = (cb.PacketsSent - ca.PacketsSent) <= 0 ? 0 : Math.Max(0.0, 1.0 - recvPackets / (cb.PacketsSent - ca.PacketsSent));
            double matchesPlaying = MetricsReader.Get(b, "arena_matches_playing");

            _out.WriteLine("matches playing: " + Num(matchesPlaying) + " (min " + Num(minPlaying) + "), sim ticks/s " + Num(simTicks / seconds)
                + " (" + Num(simTicks / seconds / Arena.Core.Sim.SimLimits.TickRate) + " match-seconds per second), skipped ticks " + Num(skipped));
            _out.WriteLine("server cpu: " + Num(cores) + " cores (" + Num(cores * 100.0) + " % of one core), "
                + Num((cpuB - cpuA).TotalMilliseconds / seconds / Math.Max(1.0, matchesPlaying)) + " ms cpu per match-second"
                + ", gc gen0 " + Num(gen0) + " gen2 " + Num(gen2) + " in the window");
            _out.WriteLine("tick work p99 max " + Ms(maxWorkP99) + " ms (max " + Ms(maxWork) + " ms), lateness p99 max " + Ms(maxLateP99)
                + " ms (max " + Ms(maxLate) + " ms), budget 33.333 ms");
            _out.WriteLine("server memory: working set peak " + Mb(peakWorking) + " MB, private peak " + Mb(peakPrivate) + " MB, gc heap peak "
                + Mb((long)peakHeap) + " MB (" + Num(peakPrivate / 1048576.0 / Math.Max(1.0, matchesPlaying)) + " MB private per match)");
            _out.WriteLine("traffic: out " + Num(sentBytes / seconds / 1024.0) + " KB/s (" + Num(sentBytes / seconds / 1024.0 / Math.Max(1.0, matchesPlaying))
                + " per match, " + Num(sentBytes / seconds * 8 / 1e6) + " Mbit/s), in " + Num(recvBytes / seconds / 1024.0) + " KB/s, packets out "
                + Num(sentPackets / seconds) + "/s in " + Num(recvPackets / seconds) + "/s");
            _out.WriteLine("loopback loss: down " + Pct(downLoss) + ", up " + Pct(upLoss) + "; late inputs " + Num(lateInputs)
                + ", missing input share " + Pct(MetricsReader.Get(b, "arena_missing_input_share")) + "; client process cpu " + Num(clientCores) + " cores");
            _out.WriteLine("server log errors: " + server.ErrorLines + "; client malformed " + (cb.Malformed - ca.Malformed)
                + ", missing baselines " + (cb.MissingBaselines - ca.MissingBaselines));

            string failure = null;
            if (skipped > 0) failure = Num(skipped) + " skipped ticks";
            else if (maxLateP99 * 1000.0 > _o.MaxLatenessP99Ms) failure = "tick lateness p99 " + Ms(maxLateP99) + " ms > " + Num(_o.MaxLatenessP99Ms) + " ms";
            else if (cores > _o.MaxServerCores) failure = "server cpu " + Num(cores) + " cores > " + Num(_o.MaxServerCores);
            else if (matchesPlaying < _o.Matches) failure = Num(matchesPlaying) + " of " + _o.Matches + " matches still playing at the end";
            else if (server.ErrorLines > 0) failure = server.ErrorLines + " error line(s) in the server log";
            string summary = _o.Matches + " matches, server " + Num(cores) + " cores, tick work p99 " + Ms(maxWorkP99) + " ms, lateness p99 "
                + Ms(maxLateP99) + " ms, private " + Mb(peakPrivate) + " MB, out " + Num(sentBytes / seconds * 8 / 1e6) + " Mbit/s, loss "
                + Pct(Math.Max(downLoss, upLoss));
            if (failure != null) return Fail(failure + " (" + summary + ")");
            _out.WriteLine("LOADTEST: OK, " + summary);
            return true;
        }

        /// <summary>Polls <c>/metrics</c> until the server has published a newer copy than the last seen one (uptime grew).</summary>
        private static Dictionary<string, double> FetchFresh(IPEndPoint metrics)
        {
            Dictionary<string, double> first = MetricsReader.Fetch(metrics);
            if (first == null) return null;
            double seen = MetricsReader.Get(first, "arena_uptime_seconds");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < FreshTimeoutMs)
            {
                Dictionary<string, double> m = MetricsReader.Fetch(metrics);
                if (m != null && MetricsReader.Get(m, "arena_uptime_seconds") > seen) return m;
                Thread.Sleep(FreshPollMs);
            }
            return null;
        }

        private LoadTotals ClientTotals()
        {
            var t = new LoadTotals();
            foreach (LoadClient c in _clients)
            {
                t.PacketsSent += c.PacketsSent;
                t.PacketsReceived += c.PacketsReceived;
                t.Malformed += c.Match.Malformed + c.Net.MalformedMessages;
                t.MissingBaselines += c.Match.MissingBaselines;
            }
            return t;
        }

        private int CountRunning()
        {
            int n = 0;
            foreach (LoadClient c in _clients)
                if (c.Match.Running) n++;
            return n;
        }

        private string Describe()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (LoadClient c in _clients)
            {
                string key = c.Lobby.State + (c.Match.HasStart ? "+start" : "") + (c.Match.Running ? "+running" : "") + (c.Match.HasEnded ? "+ended" : "");
                counts.TryGetValue(key, out int v);
                counts[key] = v + 1;
            }
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts) parts.Add(kv.Key + " " + kv.Value);
            return string.Join(", ", parts);
        }

        private bool Fail(string reason)
        {
            _out.WriteLine("LOADTEST: FAILED, " + reason);
            return false;
        }

        private static double Delta(Dictionary<string, double> a, Dictionary<string, double> b, string name) =>
            MetricsReader.Get(b, name) - MetricsReader.Get(a, name);

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Ms(double seconds) => (seconds * 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

        private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);

        private static string Pct(double share) => (share * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + " %";
    }
}
