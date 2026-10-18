using System.Diagnostics;
using System.Net;
using System.Threading;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Server.Transport;
using Arena.Net.Transport;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Lobby
{
    /// <summary>
    /// Region ping and LAN discovery over the virtual network (Server-Spec §6.3, S3.8): the median of 5 pings is the round trip,
    /// a region without a server stays unknown and the best region is chosen; the broadcast finds the node on a private network
    /// with its occupancy, maintenance flag and compatibility, and nothing from a public address.
    /// </summary>
    public sealed class LobbyDiscoveryTests
    {
        [Test]
        public void RegionPinger_MeasuresTheMedian_AndPicksTheBestRegion()
        {
            var rig = new LobbyRig(seed: 31, conditions: new NetworkConditions { LatencyMs = 20f });
            var settings = LobbyClientSettings.CreateDefault();
            var pinger = new RegionPinger(settings, new DeterministicNetRandom(5));
            InMemorySocket probe = rig.Network.CreateSocket(NetAddress.FromIPv4(10, 0, 2, 1, 6000));
            var regions = new[] { NetAddress.FromIPv4(10, 0, 0, 99, 7777), LobbyRig.ServerAddress };
            pinger.Start(probe, regions, regions.Length, rig.Now);
            for (int i = 0; i < 300 && pinger.IsRunning; i++)
            {
                rig.Step();
                pinger.Update(rig.Now);
            }
            Assert.IsTrue(pinger.IsDone);
            Assert.AreEqual(0, pinger.Answered(0), "no server there");
            Assert.AreEqual(-1, pinger.MedianMs(0));
            Assert.AreEqual(PingQuality.Unknown, pinger.QualityOf(0));
            Assert.AreEqual(settings.PingSamples, pinger.Answered(1));
            int ms = pinger.MedianMs(1);
            Assert.GreaterOrEqual(ms, 40, "2 × 20 ms");
            Assert.LessOrEqual(ms, 40 + 2 * 1000 / 60 + 1, "measured on the frame clock (server and client poll once a frame)");
            Assert.AreEqual(1, pinger.BestRegion());
            Assert.AreEqual(PingQuality.Good, pinger.QualityOf(1));
            Assert.AreEqual(0, pinger.StrayPackets);
        }

        [Test]
        public void PingQuality_FollowsTheBadgeThresholds()
        {
            var s = LobbyClientSettings.CreateDefault();
            Assert.AreEqual(PingQuality.Unknown, RegionPinger.Quality(-1, s));
            Assert.AreEqual(PingQuality.Good, RegionPinger.Quality(99, s));
            Assert.AreEqual(PingQuality.Fair, RegionPinger.Quality(100, s));
            Assert.AreEqual(PingQuality.Fair, RegionPinger.Quality(249, s));
            Assert.AreEqual(PingQuality.Poor, RegionPinger.Quality(250, s), "red from 250 ms (GDD §8.2)");
        }

        [Test]
        public void LanDiscovery_FindsTheNode_WithItsState()
        {
            var rig = new LobbyRig(seed: 32, conditions: new NetworkConditions { LatencyMs = 2f });
            var settings = LobbyClientSettings.CreateDefault();
            InMemorySocket probe = rig.Network.CreateSocket(NetAddress.FromIPv4(10, 0, 2, 7, 6001));
            var lan = new LanDiscovery(settings, LobbyRig.ContentHash, new DeterministicNetRandom(9));

            Run(rig, lan, probe);
            Assert.AreEqual(1, lan.Count, "one server answers (three broadcasts)");
            LanServerInfo s = lan.Get(0);
            Assert.AreEqual(LobbyRig.ServerAddress, s.Address);
            Assert.IsTrue(s.Compatible);
            Assert.IsFalse(s.Maintenance);
            Assert.AreEqual(NetProtocol.Version, s.ProtocolVersion);
            Assert.AreEqual(rig.Transport.MaxConnections, s.Capacity);
            Assert.AreEqual(0, lan.FirstJoinable());

            rig.Node.SetMaintenance(true);
            Run(rig, lan, probe);
            Assert.AreEqual(1, lan.Count);
            Assert.IsTrue(lan.Get(0).Maintenance);
            Assert.AreEqual(-1, lan.FirstJoinable(), "maintenance is not joinable");
            rig.Node.SetMaintenance(false);

            var otherBuild = new LanDiscovery(settings, LobbyRig.ContentHash ^ 1ul, new DeterministicNetRandom(10));
            Run(rig, otherBuild, probe);
            Assert.AreEqual(1, otherBuild.Count);
            Assert.IsFalse(otherBuild.Get(0).Compatible, "another content hash — update the game");
            Assert.AreEqual(-1, otherBuild.FirstJoinable());

            InMemorySocket outside = rig.Network.CreateSocket(NetAddress.FromIPv4(81, 2, 3, 4, 6002));
            var wan = new LanDiscovery(settings, LobbyRig.ContentHash, new DeterministicNetRandom(11));
            Run(rig, wan, outside);
            Assert.AreEqual(0, wan.Count, "public addresses get no answer");
        }

        [Test]
        public void PingAndDiscovery_OverRealLoopbackUdp()
        {
            var serverSocket = new PortableUdpServerSocket(new IPEndPoint(IPAddress.Loopback, 0));
            var transport = new ServerTransport(serverSocket, TransportSettings.CreateDefault(), LobbyRig.ContentHash,
                new DeterministicNetRandom(1), null);
            var probe = new UdpProbeSocket(new IPEndPoint(IPAddress.Loopback, 0));
            var clock = Stopwatch.StartNew();
            try
            {
                var settings = LobbyClientSettings.CreateDefault();
                NetAddress server = NetAddress.FromEndPoint(serverSocket.LocalEndPoint);
                var pinger = new RegionPinger(settings, new DeterministicNetRandom(2));
                pinger.Start(probe, new[] { server }, 1, Micros(clock));
                while (pinger.IsRunning && clock.ElapsedMilliseconds < 5000)
                {
                    transport.Receive(Micros(clock));
                    pinger.Update(Micros(clock));
                    Thread.Sleep(1);
                }
                Assert.AreEqual(settings.PingSamples, pinger.Answered(0), "every ping answered on loopback");
                Assert.Less(pinger.MedianMs(0), 100);

                var lan = new LanDiscovery(settings, LobbyRig.ContentHash, new DeterministicNetRandom(3));
                lan.Start(probe, server.Port, Micros(clock), server); // a known LAN address instead of the broadcast
                while (lan.IsRunning && clock.ElapsedMilliseconds < 10000)
                {
                    transport.Receive(Micros(clock));
                    lan.Update(Micros(clock));
                    Thread.Sleep(1);
                }
                Assert.AreEqual(1, lan.Count);
                Assert.IsTrue(lan.Get(0).Compatible);
            }
            finally
            {
                probe.Close();
                serverSocket.Close();
            }
        }

        private static long Micros(Stopwatch clock) => clock.ElapsedTicks * 1000000L / Stopwatch.Frequency;

        private static void Run(LobbyRig rig, LanDiscovery lan, InMemorySocket socket)
        {
            lan.Start(socket, LobbyRig.ServerAddress.Port, rig.Now);
            for (int i = 0; i < 300 && lan.IsRunning; i++)
            {
                rig.Step();
                lan.Update(rig.Now);
            }
            Assert.IsFalse(lan.IsRunning, "the search ends by its timeout");
        }
    }
}
