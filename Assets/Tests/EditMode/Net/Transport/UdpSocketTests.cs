using System.Net;
using System.Threading;
using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Server.Transport;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Transport
{
    /// <summary>Real UDP sockets on 127.0.0.1 only (no firewall prompt), Server-Spec §3.2, §4.6, §11.1.</summary>
    public sealed class UdpSocketTests
    {
        private const int WaitMs = 2000;
        private const int Rounds = 50;

        private readonly byte[] _buffer = new byte[NetLimits.ReceiveBufferBytes];

        private static bool WaitReceive(IServerDatagramSocket socket, byte[] buffer, out int length, out NetAddress from)
        {
            for (int i = 0; i < WaitMs; i++)
            {
                if (socket.TryReceive(buffer, out length, out from)) return true;
                Thread.Sleep(1);
            }
            length = 0;
            from = default;
            return false;
        }

        private static bool WaitReceive(IDatagramSocket socket, byte[] buffer, out int length)
        {
            for (int i = 0; i < WaitMs; i++)
            {
                if (socket.TryReceive(buffer, out length)) return true;
                Thread.Sleep(1);
            }
            length = 0;
            return false;
        }

        [Test]
        public void ClientSocket_OnLoopback_SendsReceives_WithoutExceptionsAndAllocations()
        {
            var server = new PortableUdpServerSocket(new IPEndPoint(IPAddress.Loopback, 0));
            var client = new UdpDatagramSocket(new IPEndPoint(IPAddress.Loopback, server.LocalEndPoint.Port));
            try
            {
                Assert.IsFalse(client.TryReceive(_buffer, out _), "nothing pending: false, no exception");
                Assert.IsFalse(server.TryReceive(_buffer, out _, out _));
                for (int i = 0; i < 100; i++) _buffer[i] = (byte)i;
                Assert.IsTrue(client.Send(_buffer, 0, 100));
                Assert.IsTrue(WaitReceive(server, _buffer, out int length, out NetAddress from));
                Assert.AreEqual(100, length);
                Assert.AreEqual(99, _buffer[99]);
                Assert.AreEqual(client.Local.Port, from.Port);
                Assert.IsTrue(server.SendTo(_buffer, 0, 50, from));
                Assert.IsTrue(WaitReceive(client, _buffer, out length));
                Assert.AreEqual(50, length);

                // Warm up, then the server sends a batch (the portable server socket allocates, that is allowed) and the
                // client's receive and send calls are measured.
                for (int i = 0; i < Rounds; i++) server.SendTo(_buffer, 0, 40, from);
                Thread.Sleep(50);
                while (client.TryReceive(_buffer, out _)) { }
                for (int i = 0; i < Rounds; i++) server.SendTo(_buffer, 0, 40, from);
                Thread.Sleep(50);
                int received = 0;
                AllocationAssert.NoGcAllocations(() =>
                {
                    while (client.TryReceive(_buffer, out _)) received++;
                    for (int i = 0; i < Rounds; i++) client.Send(_buffer, 0, 40);
                }, "UdpDatagramSocket allocates under Mono");
                Assert.AreEqual(Rounds, received);
                Assert.IsFalse(client.PathBroken);
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public void Transport_OverLoopbackUdp_ConnectsAndExchanges()
        {
            var server = new PortableUdpServerSocket(new IPEndPoint(IPAddress.Loopback, 0));
            var socket = new UdpDatagramSocket(new IPEndPoint(IPAddress.Loopback, server.LocalEndPoint.Port));
            try
            {
                TransportSettings settings = TransportSettings.CreateDefault();
                var serverHandler = new RecordingHandler();
                var clientHandler = new RecordingHandler();
                var host = new ServerTransport(server, settings, TransportRig.ContentHash, new CryptoNetRandom(), serverHandler);
                var client = new ClientTransport(settings, TransportRig.ContentHash, new CryptoNetRandom(), clientHandler);
                client.Connect(socket, ConnectPurpose.Lobby, false, default, NetClock.NowMicros());
                var message = new byte[NetLimits.MaxReliableMessageBytes];
                int sent = 0;
                for (int i = 0; i < WaitMs && (serverHandler.Reliable < 10 || clientHandler.Reliable < 10); i++)
                {
                    long now = NetClock.NowMicros();
                    host.Receive(now);
                    client.Update(now);
                    host.Update(now);
                    if (client.IsConnected && sent < 10)
                    {
                        int length = RecordingHandler.Fill(message, sent);
                        client.SendReliable(message, 0, length);
                        host.SendReliable(serverHandler.Connected, message, 0, length);
                        sent++;
                    }
                    Thread.Sleep(1);
                }
                Assert.AreEqual(ClientState.Connected, client.State);
                Assert.AreEqual(10, serverHandler.Reliable);
                Assert.AreEqual(10, clientHandler.Reliable);
                Assert.AreEqual(0, serverHandler.ReliableErrors + clientHandler.ReliableErrors);
                client.Disconnect();
                for (int i = 0; i < WaitMs && serverHandler.DisconnectedCount == 0; i++)
                {
                    host.Receive(NetClock.NowMicros());
                    Thread.Sleep(1);
                }
                Assert.AreEqual(DisconnectReason.Closed, serverHandler.LastDisconnect);
            }
            finally
            {
                socket.Close();
                server.Close();
            }
        }
    }
}
