using System;
using System.IO;
using System.Net;
using Arena.Net.Protocol;
using Arena.Net.Transport;
using Arena.Server.Host;

namespace Arena.Server
{
    /// <summary>
    /// Self-test of the transport over real UDP on 127.0.0.1 (Server-Spec S3.4 "Готово, когда"): handshake, then
    /// <see cref="PacketsPerDirection"/> packets each way between a <see cref="ClientTransport"/> on <see cref="UdpDatagramSocket"/>
    /// and a <see cref="ServerTransport"/> on the build's server socket, with a reliable message every
    /// <see cref="ReliableEvery"/> packets; all reliable messages must arrive in order, payloads may be lost only to the OS.
    /// Prints the allocations of the steady phase (exactly 0 only with the .NET 8+ socket; the portable one allocates, §4.6).
    /// </summary>
    public static class LoopbackTransportTest
    {
        private const int PacketsPerDirection = 5000;
        private const int ReliableEvery = 10;
        private const int Batch = 25;
        private const int PayloadBytes = 64;
        private const long ConnectBudgetMicros = 5000000L;
        private const long DrainBudgetMicros = 3000000L;
        private const double MinDeliveredShare = 0.99;

        private sealed class Sink : IServerTransportHandler
        {
            public int Connected = -1;
            public int Payloads;
            public int Reliable;
            public int ReliableOutOfOrder;
            public DisconnectReason Disconnected;

            public RejectReason Approve(ConnectPurpose purpose, bool hasToken, in NetToken token) => RejectReason.None;
            public void OnConnected(int connection) => Connected = connection;
            public void OnDisconnected(int connection, DisconnectReason reason) => Disconnected = reason;
            public void OnPacketAcked(int connection, ushort sequence) { }
            public void OnPayload(int connection, ushort sequence, byte[] buffer, int offset, int length) => Payloads++;

            public void OnReliableMessage(int connection, byte[] buffer, int offset, int length)
            {
                int index = buffer[offset] | (buffer[offset + 1] << 8);
                if (index != Reliable) ReliableOutOfOrder++;
                Reliable++;
            }
        }

        public static string Run(TextWriter output, ulong contentHash)
        {
            TransportSettings settings = TransportSettings.CreateDefault();
            settings.MaxConnections = 4;
            settings.PacketsPerSecond = 1000000;
            settings.BytesPerSecond = 1 << 30;
            var serverSink = new Sink();
            var clientSink = new Sink();
            IServerDatagramSocket serverSocket = ServerSocketFactory.Create(new IPEndPoint(IPAddress.Loopback, 0), out IPEndPoint bound);
            var clientSocket = new UdpDatagramSocket(new IPEndPoint(IPAddress.Loopback, bound.Port));
            try
            {
                var server = new ServerTransport(serverSocket, settings, contentHash, new CryptoNetRandom(), serverSink);
                var client = new ClientTransport(settings, contentHash, new CryptoNetRandom(), clientSink);
                long start = NetClock.NowMicros();
                client.Connect(clientSocket, ConnectPurpose.Lobby, false, default, start);
                while (!client.IsConnected && NetClock.NowMicros() - start < ConnectBudgetMicros)
                {
                    long now = NetClock.NowMicros();
                    server.Receive(now);
                    server.Update(now);
                    client.Update(now);
                    if (client.State == ClientState.Disconnected) return "handshake failed: " + client.DisconnectReason + " " + client.RejectReason;
                }
                if (!client.IsConnected || serverSink.Connected < 0) return "no connection within 5 s";
                int connection = serverSink.Connected;

                var payload = new byte[PayloadBytes];
                var message = new byte[2];
                int sentReliable = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                long phaseStart = NetClock.NowMicros();
                for (int i = 0; i < PacketsPerDirection; i++)
                {
                    long now = NetClock.NowMicros();
                    payload[0] = (byte)i;
                    if (i % ReliableEvery == 0)
                    {
                        message[0] = (byte)sentReliable;
                        message[1] = (byte)(sentReliable >> 8);
                        if (!client.SendReliable(message, 0, message.Length)) return "client reliable buffer full at " + i;
                        if (!server.SendReliable(connection, message, 0, message.Length)) return "server reliable buffer full at " + i;
                        sentReliable++;
                    }
                    client.Send(payload, payload.Length, now);
                    server.Send(connection, payload, payload.Length, now);
                    if (i % Batch == Batch - 1)
                    {
                        server.Receive(now);
                        server.Update(now);
                        client.Update(now);
                    }
                }
                long drainStart = NetClock.NowMicros();
                while ((serverSink.Reliable < sentReliable || clientSink.Reliable < sentReliable)
                       && NetClock.NowMicros() - drainStart < DrainBudgetMicros)
                {
                    long now = NetClock.NowMicros();
                    server.Receive(now);
                    server.Update(now);
                    client.Update(now);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                double seconds = (NetClock.NowMicros() - phaseStart) / 1e6;
                NetConnection sc = server.GetConnection(connection);
                output.WriteLine("        loopback (" + ServerSocketFactory.Kind + "): " + (2 * PacketsPerDirection) + " packets in " +
                    seconds.ToString("0.000") + " s, server got " + serverSink.Payloads + ", client got " + clientSink.Payloads +
                    ", reliable " + serverSink.Reliable + "/" + clientSink.Reliable + " of " + sentReliable + ", RTT " +
                    (sc.Rtt.SmoothedMicros / 1000.0).ToString("0.000") + " ms, allocated " + allocated + " B");
                if (serverSink.ReliableOutOfOrder > 0 || clientSink.ReliableOutOfOrder > 0) return "reliable messages out of order";
                if (serverSink.Reliable != sentReliable || clientSink.Reliable != sentReliable) return "reliable messages lost";
                if (serverSink.Payloads < PacketsPerDirection * MinDeliveredShare || clientSink.Payloads < PacketsPerDirection * MinDeliveredShare)
                    return "too many payloads lost on loopback";

                client.Disconnect();
                long closeStart = NetClock.NowMicros();
                while (serverSink.Disconnected == DisconnectReason.None && NetClock.NowMicros() - closeStart < ConnectBudgetMicros)
                    server.Receive(NetClock.NowMicros());
                if (serverSink.Disconnected != DisconnectReason.Closed) return "server did not see the disconnect: " + serverSink.Disconnected;
#if NET8_0_OR_GREATER
                if (allocated != 0) return "allocated " + allocated + " bytes with the .NET 8+ socket";
#endif
                return null;
            }
            finally
            {
                clientSocket.Close();
                serverSocket.Close();
            }
        }
    }
}
