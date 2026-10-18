using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Gameplay.Settings;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Transport;
using Arena.Online;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Online
{
    /// <summary>Pieces of the Unity online client (S3.10b): NetConfig conversion, rejoin store, dev network simulator.</summary>
    public sealed class OnlineClientTests
    {
        [Test]
        public void SeededNetConfig_ConvertsToTheSpecDefaults_AndReferencesThePack()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db.Net, "GameDatabase.Net");
            Assert.IsNotNull(db.Net.ContentPack, "content pack");
            Assert.Greater(db.Net.Regions.Count, 0);
            Assert.IsTrue(db.Net.Regions[0].IsLan, "default region — LAN (§10.1)");
            Assert.Greater(db.Net.Regions[0].Port, 0);
            Assert.IsFalse(db.Net.DevSimEnabled, "dev simulator is off by default");

            ClientMatchSettings m = NetConfigConverter.ToClientMatchSettings(db.Net);
            ClientMatchSettings d = ClientMatchSettings.CreateDefault();
            Assert.IsFalse(m.ReadyOnMatchStart, "the game answers MatchStart after the scene loads");
            Assert.AreEqual(d.TargetInputMarginTicks, m.TargetInputMarginTicks);
            Assert.AreEqual(d.MaxResimTicks, m.MaxResimTicks);
            Assert.AreEqual(d.MaxTicksPerFrame, m.MaxTicksPerFrame);
            Assert.AreEqual(d.InterpDelaySnapshots, m.InterpDelaySnapshots);
            Assert.AreEqual(d.InterpDelayMaxMs, m.InterpDelayMaxMs);
            Assert.AreEqual(d.CorrectionSmoothingSeconds, m.CorrectionSmoothingSeconds);
            Assert.AreEqual(d.MaxExtrapolationMs, m.MaxExtrapolationMs);

            LobbyClientSettings l = NetConfigConverter.ToLobbySettings(db.Net);
            LobbyClientSettings ld = LobbyClientSettings.CreateDefault();
            Assert.AreEqual(ld.DiscoverRepeats, l.DiscoverRepeats);
            Assert.AreEqual(ld.PingSamples, l.PingSamples);
            Assert.AreEqual(ld.PingFairMs, l.PingFairMs);
        }

        [Test]
        public void RejoinStore_RoundTrips_ExpiresAfterTheWindow_AndClears()
        {
            var kv = new MemoryKeyValueStore();
            var store = new RejoinStore(kv, 60f);
            Assert.IsFalse(store.TryLoad(1000, out _), "empty");
            var ticket = new RejoinTicket
            {
                Server = NetAddress.FromIPv4(192, 168, 1, 20, 7777),
                MatchId = 0xFEDCBA9876543210ul,
                Slot = 4,
                Token = new NetToken(0x8000000000000001ul, 0x00000000FFFFFFFFul),
                SavedAtSeconds = 1000
            };
            store.Save(in ticket);
            Assert.IsTrue(store.TryLoad(1030, out RejoinTicket back));
            Assert.AreEqual(ticket.Server, back.Server);
            Assert.AreEqual(ticket.MatchId, back.MatchId);
            Assert.AreEqual(ticket.Slot, back.Slot);
            Assert.AreEqual(ticket.Token, back.Token);
            Assert.AreEqual(ticket.SavedAtSeconds, back.SavedAtSeconds);
            Assert.IsFalse(store.TryLoad(1061, out _), "older than the rejoin window");
            Assert.IsFalse(store.TryLoad(999, out _), "saved in the future (clock changed)");
            store.Clear();
            Assert.IsFalse(store.TryLoad(1030, out _), "cleared");
        }

        [Test]
        public void LagSimulator_DelaysBothDirections_InOrderOfDueTime()
        {
            long now = 0;
            var inner = new FakeDatagramSocket();
            var sim = new LagSimulatorSocket(inner, 50, 0, 0f, 8, 1, () => now);
            var packet = new byte[] { 1, 2, 3 };
            Assert.IsTrue(sim.Send(packet, 0, packet.Length));
            Assert.AreEqual(0, inner.Sent.Count, "held back");
            now = 49000;
            var buffer = new byte[2048];
            Assert.IsFalse(sim.TryReceive(buffer, out _));
            Assert.AreEqual(0, inner.Sent.Count);
            now = 50000;
            sim.TryReceive(buffer, out _);
            Assert.AreEqual(1, inner.Sent.Count, "sent after 50 ms");
            CollectionAssert.AreEqual(packet, inner.Sent[0]);

            inner.ToReceive.Enqueue(new byte[] { 9 });
            Assert.IsFalse(sim.TryReceive(buffer, out _), "incoming held back");
            inner.ToReceive.Enqueue(new byte[] { 8, 8 });
            now = 99999;
            Assert.IsFalse(sim.TryReceive(buffer, out _));
            now = 100000;
            Assert.IsTrue(sim.TryReceive(buffer, out int length));
            Assert.AreEqual(1, length);
            Assert.AreEqual(9, buffer[0]);
            Assert.IsFalse(sim.TryReceive(buffer, out _), "the second one arrived later");
            now = 149999;
            Assert.IsTrue(sim.TryReceive(buffer, out length));
            Assert.AreEqual(2, length);
        }

        [Test]
        public void LagSimulator_FullLoss_DropsEverything_AndAFullQueueDrops()
        {
            long now = 0;
            var inner = new FakeDatagramSocket();
            var lossy = new LagSimulatorSocket(inner, 0, 0, 100f, 4, 1, () => now);
            for (int i = 0; i < 10; i++) Assert.IsTrue(lossy.Send(new byte[] { 1 }, 0, 1), "a lost datagram looks sent");
            Assert.AreEqual(0, inner.Sent.Count);
            Assert.AreEqual(10, lossy.Dropped);

            var small = new LagSimulatorSocket(inner, 100, 0, 0f, 2, 1, () => now);
            for (int i = 0; i < 3; i++) small.Send(new byte[] { 1 }, 0, 1);
            Assert.AreEqual(1, small.Dropped, "two slots: the third datagram is dropped");
        }
    }
}
