using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Net.Client;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Transport;
using Arena.Server.Host;

namespace Arena.Server
{
    /// <summary>
    /// Self-test of the real server process (Server-Spec S3.9, §11.2): <see cref="ServerHost"/> on 127.0.0.1 (random UDP and
    /// metrics ports) on its own game thread, and <see cref="Clients"/> console clients in this thread (the same
    /// <see cref="NetClient"/> + <see cref="LobbyClient"/> + <see cref="MatchClientSession"/> as the game, each played by a
    /// <see cref="ClientBot"/> reading its display state) at 60 frames/s of real time. They search Acorns, get one full 3×3
    /// match and play it shortened to <c>--match-seconds</c> (the mode length of the shared in-memory content — server and
    /// clients alike; the pack hash does not change). Mid-match <c>/metrics</c> and <c>/healthz</c> are fetched (numbers with a
    /// dot) and <see cref="ShutdownHandler.RequestStop"/> is called: a new lobby client is refused (<c>Maintenance</c>), the
    /// match plays to the end, every client's <c>MatchEnded</c> equals the server's line in <c>matches.jsonl</c>, the game thread
    /// exits on its own. Also: no exception and no error line, tick lateness p99 &lt; 2 ms.
    /// </summary>
    public static class HostSelfTest
    {
        public const int Clients = 6;
        public const float DefaultMatchSeconds = 20f;

        private const long FrameMicros = 1000000L / 60L;
        private const long MetricsAfterMicros = 4L * 1000000L;
        private const long LateClientBudgetMicros = 3L * 1000000L;
        private const float ExtraSeconds = 45f;
        private const int JoinBudgetMs = 15000;
        private const long MaxLatenessP99Micros = 2000;
        private const float SelfTestDrainSeconds = 60f;
        private const float SelfTestSummarySeconds = 5f;

        private sealed class Client
        {
            public NetClient Net;
            public LobbyClient Lobby;
            public MatchClientSession Session;
            public UdpDatagramSocket Socket;
            public ClientBot Bot;
        }

        public static string Run(TextWriter output, ServerContent content, float matchSeconds)
        {
            ContentBundle bundle = content.Bundle;
            int savedDuration = bundle.Acorns.DurationTicks;
            bundle.Acorns.DurationTicks = Math.Max(1, (int)Math.Round(matchSeconds * Arena.Core.Sim.SimLimits.TickRate));
            try
            {
                return RunMatch(output, content, matchSeconds);
            }
            finally
            {
                bundle.Acorns.DurationTicks = savedDuration;
            }
        }

        private static string RunMatch(TextWriter output, ServerContent content, float matchSeconds)
        {
            ContentBundle bundle = content.Bundle;
            string dir = Path.Combine(AppContext.BaseDirectory, "selftest");
            Directory.CreateDirectory(dir);
            string resultsPath = Path.Combine(dir, "matches.jsonl");
            if (File.Exists(resultsPath)) File.Delete(resultsPath);

            ServerConfig config = ServerConfig.CreateDefault();
            config.Host.BindAddress = "127.0.0.1";
            config.Host.UdpPort = 0;
            config.Host.MetricsBindAddress = "127.0.0.1";
            config.Host.MetricsPort = 0;
            config.Host.MatchLogPath = resultsPath;
            config.Host.MaintenanceFlagFile = "";
            config.Host.DrainSeconds = SelfTestDrainSeconds;
            config.Host.SummaryIntervalSeconds = SelfTestSummarySeconds;
            config.Transport.LanDiscovery = false;
            var logText = new StringWriter(CultureInfo.InvariantCulture);
            var log = new ConsoleLog(TextWriter.Synchronized(logText), LogLevel.Info);
            var shutdown = new ShutdownHandler();
            BotDifficultyDef novice = bundle.FindBotDifficulty(BotDifficulty.Novice) ?? bundle.BotDifficulties[0];

            var clients = new Client[Clients];
            Client late = null;
            using (var host = new ServerHost(config, content, log, shutdown))
            {
                try
                {
                    host.Start();
                    host.RunOnThread();
                    var server = new IPEndPoint(IPAddress.Loopback, host.UdpEndPoint.Port);
                    long now = NetClock.NowMicros();
                    for (int k = 0; k < Clients; k++)
                    {
                        var net = new NetClient(config.Transport, content.Hash, new CryptoNetRandom());
                        clients[k] = new Client
                        {
                            Net = net,
                            Lobby = new LobbyClient(net),
                            Session = new MatchClientSession(net, bundle),
                            Socket = new UdpDatagramSocket(server),
                        };
                        clients[k].Lobby.Search(clients[k].Socket, ModeKind.Acorns, k % bundle.Fighters.Length, now);
                    }

                    string metricsError = "not fetched";
                    bool stopRequested = false;
                    long stopAt = 0;
                    long runningSince = 0;
                    long start = NetClock.NowMicros();
                    long end = start + (long)((matchSeconds + ExtraSeconds) * 1000000.0);
                    long nextFrame = start;
                    while (true)
                    {
                        now = NetClock.NowMicros();
                        if (now > end) return "timeout: " + Describe(clients);
                        if (host.Failure != null) return "game thread crashed: " + host.Failure.GetType().Name + ": " + host.Failure.Message;
                        bool allEnded = true;
                        bool allRunning = true;
                        for (int k = 0; k < Clients; k++)
                        {
                            Frame(clients[k], bundle, novice, k, now);
                            allEnded &= clients[k].Session.HasEnded;
                            allRunning &= clients[k].Session.HasStart && clients[k].Session.NewestServerTick > 0;
                        }
                        if (late != null) Frame(late, bundle, novice, Clients, now);
                        if (allRunning && runningSince == 0) runningSince = now;
                        if (!stopRequested && runningSince != 0 && now - runningSince >= MetricsAfterMicros)
                        {
                            metricsError = CheckMetrics(host.MetricsEndPoint);
                            shutdown.RequestStop("self-test");
                            stopRequested = true;
                            stopAt = NetClock.NowMicros();
                        }
                        if (stopRequested && late == null && NetClock.NowMicros() - stopAt > 500000L)
                        {
                            // The node has seen the stop by now: a new search must be refused.
                            var net = new NetClient(config.Transport, content.Hash, new CryptoNetRandom());
                            late = new Client { Net = net, Lobby = new LobbyClient(net), Session = new MatchClientSession(net, bundle), Socket = new UdpDatagramSocket(server) };
                            late.Lobby.Search(late.Socket, ModeKind.Acorns, 0, NetClock.NowMicros());
                            stopAt = NetClock.NowMicros();
                        }
                        if (allEnded && late != null && (late.Lobby.State == LobbyState.Failed || late.Lobby.State == LobbyState.Rejected
                            || NetClock.NowMicros() - stopAt > LateClientBudgetMicros)) break;
                        nextFrame += FrameMicros;
                        SleepUntil(nextFrame);
                    }
                    if (metricsError != null) return "metrics: " + metricsError;
                    if (late.Lobby.ConnectRejectReason != RejectReason.Maintenance)
                        return "a search after the stop request was not refused (lobby " + late.Lobby.State + ", reject " + late.Lobby.ConnectRejectReason + ")";

                    // The game thread drains and exits by itself; keep the clients answering meanwhile.
                    long joinEnd = NetClock.NowMicros() + JoinBudgetMs * 1000L;
                    while (!host.Join(0))
                    {
                        now = NetClock.NowMicros();
                        if (now > joinEnd) return "the server did not exit after the match (stop reason " + (host.StopReason ?? "none") + ")";
                        for (int k = 0; k < Clients; k++) Frame(clients[k], bundle, novice, k, now);
                        SleepUntil(now + FrameMicros);
                    }
                    if (host.Failure != null) return "game thread crashed: " + host.Failure.GetType().Name + ": " + host.Failure.Message;
                    if (host.StopReason != "drained") return "stop reason " + host.StopReason;
                    if (log.Errors != 0) return log.Errors + " error line(s) in the server log:\n" + logText;

                    string resultError = CheckResults(resultsPath, clients);
                    if (resultError != null) return resultError;
                    for (int k = 0; k < Clients; k++)
                        if (clients[k].Net.MalformedMessages != 0 || clients[k].Session.MalformedSnapshots != 0)
                            return "client " + k + ": malformed messages " + clients[k].Net.MalformedMessages + ", snapshots " + clients[k].Session.MalformedSnapshots;

                    string summary = CountLines(logText.ToString(), " summary: ") + " summary line(s)";
                    long p99 = host.TickLateness.PercentileMicros(0.99);
                    var m = host.Node.Matches.Metrics;
                    output.WriteLine("        host: " + ServerSocketFactory.Kind + ", timer 1 ms " + (host.TimerResolutionActive ? "on" : "n/a") + ", Sleep(1) " + Ms(host.SleepMicros) + " ms"
                        + ", " + host.Node.Matches.Metrics.Ticks + " sim ticks, due passes " + host.TickLateness.Count
                        + ", lateness p50 " + Ms(host.TickLateness.PercentileMicros(0.5)) + " / p99 " + Ms(p99) + " / max " + Ms(host.TickLateness.MaxMicros)
                        + " ms, work p99 " + Ms(host.TickWork.PercentileMicros(0.99)) + " ms, snapshot avg "
                        + m.AverageSnapshotBytes.ToString("F0", CultureInfo.InvariantCulture) + " B, missing inputs "
                        + (m.MissingInputShare * 100.0).ToString("F2", CultureInfo.InvariantCulture) + " %, " + summary + ", stop: " + host.StopReason);
                    if (p99 >= MaxLatenessP99Micros) return "tick lateness p99 " + Ms(p99) + " ms >= 2 ms";
                    if (host.Node.Matches.Metrics.SkippedTicks != 0) return host.Node.Matches.Metrics.SkippedTicks + " skipped ticks";
                    return null;
                }
                finally
                {
                    if (!shutdown.ForceRequested)
                    {
                        shutdown.RequestStop("self-test cleanup");
                        shutdown.RequestStop("self-test cleanup");
                    }
                    host.Join(5000);
                    for (int k = 0; k < Clients; k++) clients[k]?.Socket.Close();
                    late?.Socket.Close();
                }
            }
        }

        private static void Frame(Client c, ContentBundle bundle, BotDifficultyDef novice, int k, long now)
        {
            c.Net.Update(now);
            c.Lobby.Update(now);
            MatchClientSession s = c.Session;
            int ticks = s.BeginFrame(now);
            if (c.Bot == null && s.HasStart && s.OwnSlot >= 0) c.Bot = new ClientBot(bundle.BotBehaviour, novice, s.OwnSlot, 1000 + k);
            for (int t = 0; t < ticks; t++)
            {
                PlayerInput input = c.Bot != null ? c.Bot.Think(s.Presentation, s.Defs) : default;
                s.Step(in input, now);
            }
        }

        private static void SleepUntil(long micros)
        {
            while (true)
            {
                long remaining = micros - NetClock.NowMicros();
                if (remaining <= 0) return;
                if (remaining > 1500) Thread.Sleep(1);
                else Thread.SpinWait(20);
            }
        }

        private static string CheckMetrics(IPEndPoint endPoint)
        {
            if (endPoint == null) return "endpoint is off";
            string metrics = HttpGet(endPoint, "/metrics", out int status);
            if (status != 200) return "/metrics status " + status;
            bool uptimeWithDot = false;
            int values = 0;
            foreach (string raw in metrics.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int space = line.LastIndexOf(' ');
                if (space < 0) return "bad line: " + line;
                string value = line.Substring(space + 1);
                if (value.IndexOf(',') >= 0 || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return "not an invariant number: " + line;
                values++;
                if (line.StartsWith("arena_uptime_seconds ", StringComparison.Ordinal) && value.IndexOf('.') > 0) uptimeWithDot = true;
                if (line == "arena_matches_playing 0") return "the running match is missing in the metrics";
            }
            if (values < 20) return "only " + values + " values";
            if (!uptimeWithDot) return "arena_uptime_seconds has no fractional part with a dot";
            string health = HttpGet(endPoint, "/healthz", out status);
            if (status != 200 || health.Trim() != "ok") return "/healthz: " + status + " " + health.Trim();
            HttpGet(endPoint, "/nothing", out status);
            if (status != 404) return "unknown path gave " + status;
            return null;
        }

        private static string HttpGet(IPEndPoint endPoint, string path, out int status)
        {
            using (var tcp = new TcpClient())
            {
                tcp.ReceiveTimeout = 3000;
                tcp.SendTimeout = 3000;
                tcp.Connect(endPoint);
                NetworkStream stream = tcp.GetStream();
                byte[] request = Encoding.ASCII.GetBytes("GET " + path + " HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
                stream.Write(request, 0, request.Length);
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                string response = Encoding.UTF8.GetString(buffer.ToArray());
                int headerEnd = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                string[] statusLine = response.Substring(0, Math.Max(0, response.IndexOf('\r'))).Split(' ');
                status = statusLine.Length > 1 && int.TryParse(statusLine[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s : -1;
                return headerEnd < 0 ? "" : response.Substring(headerEnd + 4);
            }
        }

        private static string CheckResults(string path, Client[] clients)
        {
            if (!File.Exists(path)) return "no " + path;
            string[] lines = File.ReadAllLines(path);
            if (lines.Length != 1) return lines.Length + " result lines, expected 1";
            using (JsonDocument doc = JsonDocument.Parse(lines[0]))
            {
                JsonElement root = doc.RootElement;
                int endTick = root.GetProperty("endTick").GetInt32();
                int winner = root.GetProperty("winnerTeam").GetInt32();
                int slots = root.GetProperty("slots").GetArrayLength();
                if (slots != Clients) return "result has " + slots + " slots";
                foreach (JsonElement slot in root.GetProperty("slots").EnumerateArray())
                    if (!slot.GetProperty("human").GetBoolean()) return "a slot is a bot in a match of " + Clients + " clients";
                if (lines[0].IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0) return "the result line contains an IP address";
                for (int k = 0; k < clients.Length; k++)
                {
                    var e = clients[k].Session.Ended;
                    if (e.EndTick != endTick || e.WinnerTeam != winner)
                        return "client " + k + " result (" + e.EndTick + ", " + e.WinnerTeam + ") differs from the server's (" + endTick + ", " + winner + ")";
                }
            }
            return null;
        }

        private static string Describe(Client[] clients)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < clients.Length; k++)
                sb.Append(" c").Append(k).Append(": ").Append(clients[k].Lobby.State).Append(clients[k].Session.HasStart ? " started" : "")
                    .Append(clients[k].Session.HasEnded ? " ended" : "").Append(" tick ").Append(clients[k].Session.NewestServerTick).Append(';');
            return sb.ToString();
        }

        private static int CountLines(string text, string marker)
        {
            int n = 0;
            foreach (string line in text.Split('\n'))
                if (line.IndexOf(marker, StringComparison.Ordinal) >= 0) n++;
            return n;
        }

        private static string Ms(long micros) => (micros / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
    }
}
