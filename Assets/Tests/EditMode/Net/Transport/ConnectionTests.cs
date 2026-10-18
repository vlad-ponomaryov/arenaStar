using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Transport;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Transport
{
    /// <summary>Live connections on the virtual network: reliability under loss, liveness, address change, limits (§3.2, §11.1).</summary>
    public sealed class ConnectionTests
    {
        private const long Second = 1000000L;
        private readonly byte[] _buffer = new byte[NetLimits.ReceiveBufferBytes];
        private readonly byte[] _message = new byte[NetLimits.MaxReliableMessageBytes];
        private readonly byte[] _payload = new byte[64];

        private static NetworkConditions Hostile() => new NetworkConditions
        {
            LatencyMs = 40f,
            JitterMs = 30f,
            LossPercent = 20f,
            DuplicatePercent = 5f,
            ReorderPercent = 10f,
            ReorderExtraMs = 60f,
        };

        [Test]
        public void ReliableChannel_Under20PercentLossDuplicatesAndReordering_DeliversAllInOrderOnce()
        {
            TransportSettings settings = TransportSettings.CreateDefault();
            settings.BytesPerSecond = 1 << 20; // full packets 30+ times a second both ways
            settings.PacketsPerSecond = 1000;
            var rig = new TransportRig(seed: 3, settings: settings);
            ClientTransport client = rig.AddClient(1, out InMemoryClientSocket socket, out RecordingHandler clientHandler);
            Assert.IsTrue(rig.RunUntilConnected());
            int connection = rig.ServerHandler.Connected;
            socket.Conditions = Hostile();
            rig.ServerSocket.Conditions = Hostile();
            const int total = 300;
            int sentUp = 0, sentDown = 0;
            long end = rig.Now + 120 * Second;
            while ((rig.ServerHandler.Reliable < total || clientHandler.Reliable < total) && rig.Now < end)
            {
                while (sentUp < total && client.SendReliable(_message, 0, RecordingHandler.Fill(_message, sentUp))) sentUp++;
                while (sentDown < total && rig.Server.SendReliable(connection, _message, 0, RecordingHandler.Fill(_message, sentDown))) sentDown++;
                client.Send(_payload, _payload.Length, rig.Now);
                rig.Server.Send(connection, _payload, _payload.Length, rig.Now);
                rig.Step(Second / 30);
            }
            NetConnection sc = rig.Server.Connections.ActiveCount > 0 ? rig.Server.GetConnection(connection) : null;
            string info = " state " + client.State + " " + client.DisconnectReason + ", up " + sentUp + "/" + rig.ServerHandler.Reliable + ", down " + sentDown + "/" + clientHandler.Reliable
                + ", server active " + rig.Server.Connections.ActiveCount + ", last disc " + rig.ServerHandler.LastDisconnect
                + ", srv rx " + (sc == null ? -1 : sc.PacketsReceived) + " dup " + (sc == null ? -1 : sc.DuplicatesDropped) + " mal " + rig.Server.Counters.DroppedMalformed
                + " rl " + rig.Server.Counters.DroppedRateLimited + ", cli rx " + client.Connection.PacketsReceived + " cli tx " + client.Connection.PacketsSent;
            Assert.AreEqual(total, rig.ServerHandler.Reliable, "client → server" + info);
            Assert.AreEqual(total, clientHandler.Reliable, "server → client");
            Assert.AreEqual(0, rig.ServerHandler.ReliableErrors);
            Assert.AreEqual(0, clientHandler.ReliableErrors);
            Assert.Greater(rig.Network.LostCount, 0);
            Assert.Greater(rig.Network.DuplicatedCount, 0);
            Assert.Greater(rig.Server.GetConnection(connection).DuplicatesDropped, 0, "duplicates are dropped by the replay window");
            Assert.AreEqual(ClientState.Connected, client.State);
        }

        [Test]
        public void Rtt_IsMeasured()
        {
            var rig = new TransportRig();
            ClientTransport client = rig.AddClient(1, out InMemoryClientSocket socket, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            socket.Conditions = new NetworkConditions { LatencyMs = 50f };
            rig.ServerSocket.Conditions = new NetworkConditions { LatencyMs = 50f };
            int connection = rig.ServerHandler.Connected;
            for (int i = 0; i < 120; i++)
            {
                client.Send(_payload, 8, rig.Now);
                rig.Server.Send(connection, _payload, 8, rig.Now);
                rig.Step(Second / 30);
            }
            long rtt = client.Connection.Rtt.SmoothedMicros;
            // 100 ms of flight + up to one 33 ms step at each end before the packet is processed.
            Assert.That(rtt, Is.InRange(95000L, 170000L), "client RTT");
            Assert.That(rig.Server.GetConnection(connection).Rtt.SmoothedMicros, Is.InRange(95000L, 170000L), "server RTT");
        }

        [Test]
        public void Keepalive_KeepsAnIdleConnection_AndTimeoutEndsADeadOne()
        {
            var rig = new TransportRig();
            ClientTransport client = rig.AddClient(1, out InMemoryClientSocket socket, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            long sentBefore = client.Connection.PacketsSent;
            rig.Run(20 * Second);
            Assert.AreEqual(ClientState.Connected, client.State, "idle but alive");
            Assert.AreEqual(1, rig.Server.Connections.ActiveCount);
            long keepalives = client.Connection.PacketsSent - sentBefore;
            Assert.GreaterOrEqual(keepalives, 18);
            Assert.LessOrEqual(keepalives, 25);

            // The client's network dies: both ends time out.
            socket.Conditions = new NetworkConditions { LossPercent = 100f };
            rig.ServerSocket.Conditions = new NetworkConditions { LossPercent = 100f };
            rig.Run(rig.Settings.DisconnectTimeoutMs * 1000L + Second);
            Assert.AreEqual(ClientState.Disconnected, client.State);
            Assert.AreEqual(DisconnectReason.Timeout, client.DisconnectReason);
            Assert.AreEqual(DisconnectReason.Timeout, rig.ServerHandler.LastDisconnect);
            Assert.AreEqual(0, rig.Server.Connections.ActiveCount);
            Assert.AreEqual(1, rig.Server.Counters.Timeouts);
        }

        [Test]
        public void Disconnect_IsSeenByThePeer_BothWays()
        {
            var rig = new TransportRig();
            ClientTransport a = rig.AddClient(1, out _, out _);
            ClientTransport b = rig.AddClient(2, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            a.Disconnect();
            rig.Step();
            Assert.AreEqual(DisconnectReason.Closed, rig.ServerHandler.LastDisconnect);
            Assert.AreEqual(1, rig.Server.Connections.ActiveCount);

            int connection = rig.Server.Connections.ActiveAt(0);
            rig.Server.Disconnect(connection, DisconnectReason.ServerShutdown);
            Assert.AreEqual(0, rig.Server.Connections.ActiveCount);
            rig.Step();
            Assert.AreEqual(ClientState.Disconnected, b.State);
            Assert.AreEqual(DisconnectReason.ServerShutdown, b.DisconnectReason);
        }

        [Test]
        public void AddressChange_MovesTheConnectionOnlyAfterPathValidation()
        {
            var rig = new TransportRig();
            ClientTransport client = rig.AddClient(1, out InMemoryClientSocket socket, out RecordingHandler clientHandler);
            Assert.IsTrue(rig.RunUntilConnected());
            int connection = rig.ServerHandler.Connected;
            NetConnection server = rig.Server.GetConnection(connection);

            // Android switched networks: the old socket breaks, the client recreates it on a new address (same connection id).
            socket.BreakPath();
            client.Send(_payload, 8, rig.Now);
            Assert.IsTrue(client.PathBroken);
            NetAddress newAddress = TransportRig.ClientAddress(1, generation: 1);
            InMemoryClientSocket replacement = rig.Network.CreateClientSocket(newAddress, TransportRig.ServerAddress);
            client.ReplaceSocket(replacement);
            client.Send(_payload, 8, rig.Now);
            rig.Step();
            rig.Step();
            rig.Step();
            Assert.AreEqual(newAddress, server.Address);
            Assert.AreEqual(1, rig.Server.Counters.PathChanges);
            Assert.GreaterOrEqual(client.PathResponsesSent, 1);

            int before = clientHandler.Payloads;
            rig.Server.Send(connection, _payload, 8, rig.Now);
            rig.Step();
            Assert.AreEqual(before + 1, clientHandler.Payloads, "the server now sends to the new address");
            Assert.AreEqual(ClientState.Connected, client.State);
        }

        [Test]
        public void ReplayedPacket_IsDropped_AndDoesNotMoveTheConnection()
        {
            var rig = new TransportRig();
            byte[] captured = null;
            int capturedLength = 0;
            rig.Network.SendFilter = (from, to, buffer, offset, length) =>
            {
                if (captured == null && to == TransportRig.ServerAddress && RawPackets.KindOf(buffer, length) == PacketKind.Data && length > NetProtocol.HeaderBytes)
                {
                    captured = new byte[length];
                    System.Buffer.BlockCopy(buffer, offset, captured, 0, length);
                    capturedLength = length;
                }
                return true;
            };
            ClientTransport client = rig.AddClient(1, out InMemoryClientSocket socket, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            client.Send(_payload, 8, rig.Now);
            rig.Step();
            Assert.IsNotNull(captured);
            Assert.AreEqual(1, rig.ServerHandler.Payloads);
            NetConnection server = rig.Server.GetConnection(rig.ServerHandler.Connected);

            socket.Send(captured, 0, capturedLength); // the same datagram again from the client's address
            InMemoryClientSocket attacker = rig.Network.CreateClientSocket(NetAddress.FromIPv4(198, 51, 100, 7, 4444), TransportRig.ServerAddress);
            attacker.Send(captured, 0, capturedLength); // and from somewhere else
            rig.Step();
            rig.Step();
            Assert.AreEqual(1, rig.ServerHandler.Payloads, "no payload twice");
            Assert.AreEqual(2, server.DuplicatesDropped);
            Assert.AreEqual(TransportRig.ClientAddress(1), server.Address);
            Assert.AreEqual(0, rig.Server.Counters.PathChallenges);
            Assert.IsFalse(attacker.TryReceive(_buffer, out _), "nothing is sent to the replaying address");
        }

        [Test]
        public void PerConnectionLimits_DropThenDisconnect()
        {
            TransportSettings settings = TransportSettings.CreateDefault();
            settings.PacketsPerSecond = 40;
            settings.RateLimitDisconnectMs = 3000;
            var rig = new TransportRig(settings: settings);
            ClientTransport client = rig.AddClient(1, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            long end = rig.Now + 10 * Second;
            while (rig.Now < end && client.State == ClientState.Connected)
            {
                client.Send(_payload, 8, rig.Now);
                client.Send(_payload, 8, rig.Now); // 120 packets per second
                rig.Step();
            }
            Assert.Greater(rig.Server.Counters.DroppedRateLimited, 0);
            Assert.AreEqual(DisconnectReason.RateLimited, rig.ServerHandler.LastDisconnect);
            Assert.AreEqual(DisconnectReason.RateLimited, client.DisconnectReason);
            Assert.AreEqual(1, rig.Server.Counters.RateLimitDisconnects);
        }

        [Test]
        public void HandshakesPerIp_AreLimited()
        {
            TransportSettings settings = TransportSettings.CreateDefault();
            settings.HandshakesPerIpPerMinute = 5;
            var rig = new TransportRig(settings: settings);
            InMemoryClientSocket raw = rig.Network.CreateClientSocket(TransportRig.ClientAddress(9), TransportRig.ServerAddress);
            for (int i = 0; i < 12; i++)
            {
                raw.Send(_buffer, 0, RawPackets.ConnectRequest(_buffer, NetProtocol.Version, TransportRig.ContentHash, (ulong)i));
                rig.Step();
            }
            Assert.AreEqual(5, rig.Server.Counters.Challenges);
            Assert.AreEqual(7, rig.Server.RateLimitedHandshakes);
        }

        [Test]
        public void Ping_IsAnsweredWithTheSameSize()
        {
            var rig = new TransportRig();
            InMemoryClientSocket raw = rig.Network.CreateClientSocket(TransportRig.ClientAddress(9), TransportRig.ServerAddress);
            raw.Send(_buffer, 0, RawPackets.Ping(_buffer, 777ul));
            rig.Step();
            rig.Step();
            Assert.IsTrue(raw.TryReceive(_buffer, out int length));
            Assert.AreEqual(PacketKind.Pong, RawPackets.KindOf(_buffer, length));
            Assert.AreEqual(NetProtocol.PingPacketBytes, length);
            Assert.IsTrue(PingMessage.Read(RawPackets.Body(_buffer, length), out PingMessage pong));
            Assert.AreEqual(777ul, pong.Nonce);

            raw.Send(_buffer, 0, RawPackets.Ping(_buffer, 1ul, padTo: 0)); // short ping: no answer
            rig.Step();
            rig.Step();
            Assert.IsFalse(raw.TryReceive(_buffer, out _));
        }

        [Test]
        public void Discover_AnswersPrivateAddressesOnly_WhenEnabled()
        {
            var rig = new TransportRig();
            InMemoryClientSocket lan = rig.Network.CreateClientSocket(NetAddress.FromIPv4(192, 168, 0, 20, 6000), TransportRig.ServerAddress);
            lan.Send(_buffer, 0, RawPackets.Discover(_buffer, 9ul));
            rig.Step();
            rig.Step();
            Assert.IsTrue(lan.TryReceive(_buffer, out int length));
            Assert.AreEqual(PacketKind.DiscoverReply, RawPackets.KindOf(_buffer, length));
            Assert.AreEqual(NetProtocol.DiscoverPacketBytes, length, "same size as the request");
            Assert.IsTrue(DiscoverReplyMessage.Read(RawPackets.Body(_buffer, length), out DiscoverReplyMessage reply));
            Assert.AreEqual(TransportRig.ContentHash, reply.ContentHash);
            Assert.AreEqual(NetProtocol.Version, reply.ProtocolVersion);
            Assert.AreEqual(9ul, reply.Nonce);

            InMemoryClientSocket wan = rig.Network.CreateClientSocket(NetAddress.FromIPv4(203, 0, 113, 1, 6000), TransportRig.ServerAddress);
            wan.Send(_buffer, 0, RawPackets.Discover(_buffer, 10ul));
            rig.Step();
            rig.Step();
            Assert.IsFalse(wan.TryReceive(_buffer, out _), "public address");

            rig.Settings.LanDiscovery = false;
            lan.Send(_buffer, 0, RawPackets.Discover(_buffer, 11ul));
            rig.Step();
            rig.Step();
            Assert.IsFalse(lan.TryReceive(_buffer, out _), "discovery off (VPS)");
        }

        [Test]
        public void ReliableWindowFull_RefusesMore_UntilAcknowledged()
        {
            var rig = new TransportRig();
            rig.AddClient(1, out InMemoryClientSocket socket, out RecordingHandler clientHandler);
            Assert.IsTrue(rig.RunUntilConnected());
            socket.Conditions = new NetworkConditions { LossPercent = 100f }; // nothing gets acknowledged
            int connection = rig.ServerHandler.Connected;
            for (int i = 0; i < NetLimits.ReliableWindow; i++)
                Assert.IsTrue(rig.Server.SendReliable(connection, _message, 0, 8));
            Assert.IsFalse(rig.Server.SendReliable(connection, _message, 0, 8), "window full");
            Assert.AreEqual(1, rig.Server.Connections.ActiveCount, "the transport does not close by itself");
            socket.Conditions = null;
            rig.Run(2000000L);
            Assert.AreEqual(NetLimits.ReliableWindow, clientHandler.Reliable);
            Assert.IsTrue(rig.Server.SendReliable(connection, _message, 0, 8), "room again");
        }
    }
}
