using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Transport;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Transport
{
    /// <summary>Handshake, cookies and refusals on the virtual network (Server-Spec §3.2, §11.1).</summary>
    public sealed class HandshakeTests
    {
        private readonly byte[] _buffer = new byte[NetLimits.ReceiveBufferBytes];

        [Test]
        public void Client_Connects_AndBothEndsAgreeOnTheConnection()
        {
            var rig = new TransportRig();
            var token = new NetToken(11ul, 22ul);
            ClientTransport client = rig.AddClient(1, out _, out _, purpose: ConnectPurpose.Rejoin, hasToken: true, token: token);
            Assert.IsTrue(rig.RunUntilConnected());
            Assert.AreEqual(ClientState.Connected, client.State);
            Assert.AreEqual(1, rig.ServerHandler.ConnectedCount);
            NetConnection server = rig.Server.GetConnection(rig.ServerHandler.Connected);
            Assert.AreEqual(client.Connection.Id, server.Id);
            Assert.AreNotEqual(0ul, server.Id);
            Assert.AreEqual(TransportRig.ClientAddress(1), server.Address);
            Assert.AreEqual(ConnectPurpose.Rejoin, rig.ServerHandler.LastPurpose);
            Assert.IsTrue(rig.ServerHandler.LastHasToken);
            Assert.AreEqual(token, rig.ServerHandler.LastToken);
            Assert.AreEqual(1, rig.Server.Counters.Challenges);
            Assert.AreEqual(1, rig.Server.Counters.Accepted);
        }

        [Test]
        public void HandshakeRequests_ArePaddedAndAnswersAreSmaller()
        {
            var rig = new TransportRig();
            int maxAnswer = 0;
            int minRequest = int.MaxValue;
            rig.Network.SendFilter = (from, to, buffer, offset, length) =>
            {
                PacketKind kind = RawPackets.KindOf(buffer, length);
                if (kind == PacketKind.ConnectRequest || kind == PacketKind.ConnectResponse) minRequest = System.Math.Min(minRequest, length);
                if (kind == PacketKind.ConnectChallenge || kind == PacketKind.ConnectAccepted) maxAnswer = System.Math.Max(maxAnswer, length);
                return true;
            };
            rig.AddClient(1, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            Assert.AreEqual(NetProtocol.HandshakePacketBytes, minRequest);
            Assert.Less(maxAnswer, minRequest);
        }

        [Test]
        public void LostConnectAccepted_IsRepeated_WithoutSecondConnection()
        {
            var rig = new TransportRig();
            int accepts = 0;
            rig.Network.SendFilter = (from, to, buffer, offset, length) =>
                RawPackets.KindOf(buffer, length) != PacketKind.ConnectAccepted || ++accepts > 2;
            ClientTransport client = rig.AddClient(1, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            Assert.AreEqual(ClientState.Connected, client.State);
            Assert.AreEqual(3, accepts, "two accepts were lost");
            Assert.AreEqual(1, rig.Server.Counters.Accepted);
            Assert.AreEqual(2, rig.Server.Counters.AcceptRepeats);
            Assert.AreEqual(1, rig.Server.Connections.ActiveCount);
            Assert.AreEqual(rig.Server.GetConnection(rig.ServerHandler.Connected).Id, client.Connection.Id);
        }

        [Test]
        public void ContentMismatch_IsRejected()
        {
            var rig = new TransportRig();
            ClientTransport client = rig.AddClient(1, out _, out _, contentHash: 0xBADul);
            Assert.IsTrue(rig.RunUntilConnected());
            Assert.AreEqual(ClientState.Disconnected, client.State);
            Assert.AreEqual(DisconnectReason.Rejected, client.DisconnectReason);
            Assert.AreEqual(RejectReason.ContentMismatch, client.RejectReason);
            Assert.AreEqual(0, rig.Server.Connections.ActiveCount);
        }

        [Test]
        public void VersionMismatch_IsRejected()
        {
            var rig = new TransportRig();
            InMemoryClientSocket raw = rig.Network.CreateClientSocket(TransportRig.ClientAddress(9), TransportRig.ServerAddress);
            int length = RawPackets.ConnectRequest(_buffer, (ushort)(NetProtocol.Version + 1), TransportRig.ContentHash, 42ul);
            raw.Send(_buffer, 0, length);
            rig.Step();
            rig.Step();
            Assert.IsTrue(raw.TryReceive(_buffer, out int answer));
            Assert.AreEqual(PacketKind.ConnectRejected, RawPackets.KindOf(_buffer, answer));
            Assert.IsTrue(ConnectRejectedMessage.Read(RawPackets.Body(_buffer, answer), out ConnectRejectedMessage rejected));
            Assert.AreEqual(RejectReason.VersionMismatch, rejected.Reason);
            Assert.AreEqual(42ul, rejected.Nonce);
        }

        [Test]
        public void ApplicationRefusal_ReachesTheClient()
        {
            var rig = new TransportRig();
            rig.ServerHandler.ApproveResult = RejectReason.Maintenance;
            ClientTransport client = rig.AddClient(1, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            Assert.AreEqual(RejectReason.Maintenance, client.RejectReason);
            Assert.AreEqual(0, rig.ServerHandler.ConnectedCount);
        }

        [Test]
        public void FullServer_RejectsServerFull()
        {
            TransportSettings settings = TransportSettings.CreateDefault();
            settings.MaxConnections = 1;
            var rig = new TransportRig(settings: settings);
            ClientTransport first = rig.AddClient(1, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            ClientTransport second = rig.AddClient(2, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            Assert.AreEqual(ClientState.Connected, first.State);
            Assert.AreEqual(RejectReason.ServerFull, second.RejectReason);
        }

        [Test]
        public void UnpaddedRequest_GetsNoAnswer()
        {
            var rig = new TransportRig();
            InMemoryClientSocket raw = rig.Network.CreateClientSocket(TransportRig.ClientAddress(9), TransportRig.ServerAddress);
            int length = RawPackets.ConnectRequest(_buffer, NetProtocol.Version, TransportRig.ContentHash, 1ul, padTo: 0);
            Assert.Less(length, NetProtocol.HandshakePacketBytes);
            raw.Send(_buffer, 0, length);
            rig.Step();
            rig.Step();
            Assert.IsFalse(raw.TryReceive(_buffer, out _));
            Assert.AreEqual(0, rig.Server.Counters.PacketsSent);
        }

        [Test]
        public void ForgedOrForeignCookie_CreatesNothing()
        {
            var rig = new TransportRig();
            InMemoryClientSocket raw = rig.Network.CreateClientSocket(TransportRig.ClientAddress(9), TransportRig.ServerAddress);
            raw.Send(_buffer, 0, RawPackets.ConnectResponse(_buffer, 5ul, 0x1234ul));
            rig.Step();
            Assert.AreEqual(1, rig.Server.Counters.BadCookies);

            // A valid cookie obtained from one address does not work from another (address proof).
            raw.Send(_buffer, 0, RawPackets.ConnectRequest(_buffer, NetProtocol.Version, TransportRig.ContentHash, 6ul));
            rig.Step();
            rig.Step();
            Assert.IsTrue(raw.TryReceive(_buffer, out int answer));
            Assert.IsTrue(ConnectChallengeMessage.Read(RawPackets.Body(_buffer, answer), out ConnectChallengeMessage challenge));
            InMemoryClientSocket other = rig.Network.CreateClientSocket(TransportRig.ClientAddress(10), TransportRig.ServerAddress);
            other.Send(_buffer, 0, RawPackets.ConnectResponse(_buffer, 6ul, challenge.Cookie));
            rig.Step();
            Assert.AreEqual(2, rig.Server.Counters.BadCookies);
            Assert.AreEqual(0, rig.Server.Connections.ActiveCount);

            // The cookie expires after two slices.
            rig.Run((long)rig.Settings.CookieSliceMs * 2000L + 1000L);
            raw.Send(_buffer, 0, RawPackets.ConnectResponse(_buffer, 6ul, challenge.Cookie));
            rig.Step();
            Assert.AreEqual(3, rig.Server.Counters.BadCookies);
            Assert.AreEqual(0, rig.Server.Connections.ActiveCount);
        }

        [Test]
        public void SilentServer_GivesConnectTimeout()
        {
            var rig = new TransportRig();
            rig.Network.SendFilter = (from, to, buffer, offset, length) => from != TransportRig.ServerAddress;
            ClientTransport client = rig.AddClient(1, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected(rig.Settings.ConnectTimeoutMs * 1000L + 500000L));
            Assert.AreEqual(DisconnectReason.ConnectTimeout, client.DisconnectReason);
            Assert.GreaterOrEqual(client.HandshakePacketsSent, rig.Settings.ConnectTimeoutMs / rig.Settings.HandshakeRetryMs - 1);
        }
    }
}
