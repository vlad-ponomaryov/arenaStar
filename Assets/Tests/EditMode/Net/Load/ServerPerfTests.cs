using System.Diagnostics;
using Arena.Core.Modes;
using Arena.Net.Content;
using Arena.Net.Server.Diagnostics;
using Arena.Net.Server.Match;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Net.Server;
using Arena.Tests.EditMode.Net.Snapshots;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Load
{
    /// <summary>
    /// Load in one process (Server-Spec §11.1, category <c>Perf</c>, S3.15): one <see cref="MatchHost"/> with 100 Acorns rooms
    /// and 6 scripted clients each over the virtual network; after the start, 20 s of virtual time. Measures the real time of
    /// <see cref="MatchHost.Tick"/> per match-second (Unity Mono, without sockets) and the snapshot bytes. Not part of the usual
    /// run (<c>Tools/test.ps1</c> excludes <c>Long</c> and <c>Perf</c>): <c>Tools/test.ps1 -Category Perf</c>. The real-socket
    /// load test of the .NET server is <c>Tools/server-load.ps1</c>.
    /// </summary>
    [Category("Perf")]
    public sealed class ServerPerfTests
    {
        private const int Matches = 100;
        private const int PlayersPerMatch = 6;
        private const float StartSeconds = 25f;
        private const float MeasureSeconds = 20f;
        private const long FrameMicros = 1000000L / 60L;
        /// <summary>Host time per match-second the test accepts: 100 matches must run in real time on one Mono thread with headroom.</summary>
        private const double MaxHostMsPerMatchSecond = 5.0;
        private const double MaxAverageSnapshotBytes = 250.0;

        [Test]
        public void HundredMatches_SixScriptedClientsEach_HostTickPerMatchSecond_AndSnapshotBytes()
        {
            var network = new InMemoryNetwork(5);
            ContentBundle bundle = SnapshotMatchRig.LoadBundle();
            ServerSettings settings = ServerSettings.CreateDefault();
            settings.MaxMatches = Matches + 1;
            TransportSettings transport = TransportSettings.CreateDefault();
            transport.HandshakesPerIpPerMinute = Matches * PlayersPerMatch * 4;
            const ulong hash = 0x5EEDu;
            var host = new MatchHost(network.CreateSocket(MatchServerRig.ServerAddress), transport, settings, bundle, hash, new DeterministicNetRandom(6));
            int mapIndex = bundle.MapsFor(ModeKind.Acorns).MapIndices[0];
            var clients = new ScriptedMatchClient[Matches * PlayersPerMatch];
            for (int m = 0; m < Matches; m++)
            {
                var fighters = new int[PlayersPerMatch];
                for (int i = 0; i < PlayersPerMatch; i++) fighters[i] = i % bundle.Fighters.Length;
                MatchRoom room = host.CreateMatch(MatchRoomRequest.FromBundle(bundle, mapIndex, ModeKind.Acorns, fighters, 100 + m), network.NowMicros);
                for (int i = 0; i < PlayersPerMatch; i++)
                {
                    int k = m * PlayersPerMatch + i;
                    int salt = k * 7 + 1;
                    var c = new ScriptedMatchClient(transport, hash, new DeterministicNetRandom(1000 + k));
                    c.Script = (tick, latest) => MatchServerRig.Wander(tick, salt);
                    var socket = network.CreateClientSocket(NetAddress.FromIPv4(10, 1, (byte)(k / 250), (byte)(k % 250 + 1), (ushort)(5000 + k)),
                        MatchServerRig.ServerAddress);
                    c.Connect(socket, room.GetSlot(i).Token, network.NowMicros);
                    clients[k] = c;
                }
            }

            Step(network, host, clients, StartSeconds, null);
            Assert.AreEqual(Matches, host.PlayingMatches, "all matches play");
            long ticks0 = host.Metrics.Ticks, snaps0 = host.Metrics.SnapshotsSent, bytes0 = host.Metrics.SnapshotBytes;
            var hostTime = new Stopwatch();
            Step(network, host, clients, MeasureSeconds, hostTime);
            double matchSeconds = (host.Metrics.Ticks - ticks0) / (double)Arena.Core.Sim.SimLimits.TickRate;
            double msPerMatchSecond = hostTime.Elapsed.TotalMilliseconds / matchSeconds;
            double avgSnapshot = (host.Metrics.SnapshotBytes - bytes0) / (double)(host.Metrics.SnapshotsSent - snaps0);
            TestContext.WriteLine("host: " + hostTime.Elapsed.TotalMilliseconds.ToString("F0") + " ms for " + matchSeconds.ToString("F0")
                + " match-seconds = " + msPerMatchSecond.ToString("F3") + " ms per match-second (" + (msPerMatchSecond * Matches / 10.0).ToString("F1")
                + " % of a core for " + Matches + " matches); snapshot avg " + avgSnapshot.ToString("F0") + " B, max " + host.Metrics.MaxSnapshotBytes
                + " B; missing input share " + host.Metrics.MissingInputShare.ToString("P2") + ", skipped ticks " + host.Metrics.SkippedTicks);
            Assert.Greater(matchSeconds, Matches * MeasureSeconds * 0.95, "every match ran the whole window");
            Assert.Less(msPerMatchSecond, MaxHostMsPerMatchSecond, "host time per match-second");
            Assert.Less(avgSnapshot, MaxAverageSnapshotBytes, "average snapshot");
            Assert.Less(host.Metrics.MissingInputShare, 0.01, "late and missing inputs");
        }

        private static void Step(InMemoryNetwork network, MatchHost host, ScriptedMatchClient[] clients, float seconds, Stopwatch hostTime)
        {
            long end = network.NowMicros + (long)(seconds * 1000000.0);
            while (network.NowMicros < end)
            {
                network.Advance(FrameMicros);
                long now = network.NowMicros;
                hostTime?.Start();
                host.Tick(now);
                hostTime?.Stop();
                for (int k = 0; k < clients.Length; k++) clients[k].Update(now);
            }
        }
    }
}
