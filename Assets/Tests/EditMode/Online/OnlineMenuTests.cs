using System.Collections.Generic;
using System.Net.Sockets;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Editor.Data;
using Arena.Net.Client;
using Arena.Net.Transport;
using Arena.Online;
using Arena.Tests.EditMode.Net.Lobby;
using Arena.UI.Menu;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Online
{
    /// <summary>
    /// Pieces of the online menu (S3.11): the region ping service finds the LAN server by broadcast and pings it, pings a region
    /// by IP, leaves an empty region unknown and picks the best; region names and every error of a failed search have texts.
    /// </summary>
    public sealed class OnlineMenuTests
    {
        private const float LatencyMs = 20f;

        [Test]
        public void RegionPingService_FindsLanAndIpRegions_AndPicksTheBest()
        {
            var rig = new LobbyRig(seed: 41, conditions: new NetworkConditions { LatencyMs = LatencyMs });
            int port = LobbyRig.ServerAddress.Port;
            string serverIp = LobbyRig.ServerAddress.ToEndPoint(AddressFamily.InterNetwork).Address.ToString();
            var regions = new List<NetRegionConfig>
            {
                new NetRegionConfig { Id = "lan", NameKey = LocKeys.RegionLan, Host = "", Port = port },
                new NetRegionConfig { Id = "empty", NameKey = LocKeys.RegionLan, Host = "10.0.0.99", Port = port },
                new NetRegionConfig { Id = "direct", NameKey = LocKeys.RegionLan, Host = serverIp, Port = port },
            };
            int sockets = 0;
            var service = new RegionPingService(regions, LobbyClientSettings.CreateDefault(), LobbyRig.ContentHash,
                new DeterministicNetRandom(3), () => rig.Network.CreateSocket(NetAddress.FromIPv4(10, 0, 2, 9, (ushort)(6100 + sockets++))));
            Assert.AreEqual(-1, service.BestRegion(), "nothing measured yet");

            Assert.IsTrue(service.Start(rig.Now));
            for (int i = 0; i < 600 && service.IsRunning; i++)
            {
                rig.Step();
                service.Update(rig.Now);
            }
            Assert.IsFalse(service.IsRunning, "the run ends");
            Assert.AreEqual(1, service.Runs);
            Assert.GreaterOrEqual(service.PingMs(0), (int)(2 * LatencyMs), "LAN server found by broadcast and pinged");
            Assert.AreEqual(-1, service.PingMs(1), "no server there");
            Assert.AreEqual(PingQuality.Unknown, service.Quality(1));
            Assert.GreaterOrEqual(service.PingMs(2), (int)(2 * LatencyMs), "region by IP pinged");
            Assert.AreEqual(PingQuality.Good, service.Quality(2));
            int best = service.BestRegion();
            Assert.IsTrue(best == 0 || best == 2, "the best region answered");

            Assert.IsTrue(service.Start(rig.Now), "a second run on a new socket");
            Assert.AreEqual(2, sockets);
            service.Stop();
            Assert.IsFalse(service.IsRunning);
        }

        [Test]
        public void RegionNames_AndSearchErrors_HaveTexts()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            LocTable table = db.Strings;
            foreach (NetRegionConfig r in db.Net.Regions) Assert.IsNotNull(table.Find(r.NameKey), "region name " + r.NameKey);
            foreach (OnlineFailure f in System.Enum.GetValues(typeof(OnlineFailure)))
            {
                if (f == OnlineFailure.None) continue;
                Assert.IsNotNull(table.Find(MenuScreen.FailureKey(f)), "error text of " + f);
            }
            Assert.AreEqual(LocKeys.ErrorUdpBlocked, MenuScreen.FailureKey(OnlineFailure.UdpBlocked));
            Assert.IsTrue(table.Find(LocKeys.QueueFound).En.Contains("{1}"), "Found N of M is a format");
            Assert.IsTrue(table.Find(LocKeys.ResultsPlace).Ru.Contains("{0}"), "the place is a format");
            Assert.Greater(db.Net.ConnectionLostSeconds, 0f);
            Assert.Greater(db.Net.HudPingRefreshSeconds, 0f);
        }
    }
}
