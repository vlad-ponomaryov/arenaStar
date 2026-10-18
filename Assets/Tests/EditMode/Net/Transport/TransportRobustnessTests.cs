using Arena.Core.Random;
using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Transport
{
    /// <summary>Fuzzing of both ends and the zero-allocation steady state (Server-Spec §4.6, §11.1).</summary>
    public sealed class TransportRobustnessTests
    {
        private const long Second = 1000000L;
        private const int CapturedMax = 64;

        private readonly byte[] _buffer = new byte[NetLimits.ReceiveBufferBytes];
        private readonly byte[] _payload = new byte[96];
        private readonly byte[] _message = new byte[NetLimits.MaxReliableMessageBytes];

        private static TransportSettings Unlimited()
        {
            TransportSettings settings = TransportSettings.CreateDefault();
            settings.PacketsPerSecond = 1000000;
            settings.BytesPerSecond = 1 << 30;
            settings.HandshakesPerIpPerMinute = 1000000;
            return settings;
        }

        [Test]
        public void Fuzz_RandomTruncatedAndFlippedPackets_NoExceptionsNoAllocations()
        {
            var rig = new TransportRig(seed: 9, settings: Unlimited());
            var captured = new byte[CapturedMax][];
            var capturedLength = new int[CapturedMax];
            int capturedCount = 0;
            rig.Network.SendFilter = (from, to, buffer, offset, length) =>
            {
                if (capturedCount < CapturedMax)
                {
                    captured[capturedCount] = new byte[length];
                    System.Buffer.BlockCopy(buffer, offset, captured[capturedCount], 0, length);
                    capturedLength[capturedCount++] = length;
                }
                return true;
            };
            ClientTransport client = rig.AddClient(1, out _, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            int connection = rig.ServerHandler.Connected;
            for (int i = 0; i < 20; i++)
            {
                client.SendReliable(_message, 0, RecordingHandler.Fill(_message, i));
                rig.Server.SendReliable(connection, _message, 0, RecordingHandler.Fill(_message, i));
                client.Send(_payload, _payload.Length, rig.Now);
                rig.Server.Send(connection, _payload, _payload.Length, rig.Now);
                rig.Step();
            }
            rig.Network.SendFilter = null;
            Assert.Greater(capturedCount, 10);
            ulong id = client.Connection.Id;
            InMemoryClientSocket attacker = rig.Network.CreateClientSocket(NetAddress.FromIPv4(203, 0, 113, 9, 999), TransportRig.ServerAddress);
            NetAddress clientAddress = TransportRig.ClientAddress(1);
            var rng = DeterministicRng.FromSeed(1234);

            void FuzzRound(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int length;
                    int mode = rng.Range(0, 4);
                    if (mode == 0)
                    {
                        length = rng.Range(0, NetLimits.MaxPacketBytes + 100);
                        for (int b = 0; b < length; b++) _buffer[b] = (byte)rng.NextUInt();
                    }
                    else
                    {
                        int k = rng.Range(0, capturedCount);
                        length = capturedLength[k];
                        System.Buffer.BlockCopy(captured[k], 0, _buffer, 0, length);
                        if (mode == 1) length = rng.Range(0, length + 1);
                        int flips = rng.Range(1, 4);
                        for (int f = 0; f < flips && length > 0; f++) _buffer[rng.Range(0, length)] ^= (byte)(1 << rng.Range(0, 8));
                        if (mode == 3 && length >= NetProtocol.HeaderBytes)
                        {
                            _buffer[0] = (byte)((_buffer[0] & 0xF0) | (int)PacketKind.Data);
                            for (int b = 0; b < 8; b++) _buffer[1 + b] = (byte)(id >> (8 * b));
                        }
                    }
                    if ((i & 1) == 0) attacker.Send(_buffer, 0, length);
                    else rig.ServerSocket.SendTo(_buffer, 0, length, clientAddress);
                    if ((i & 15) == 15) rig.Step();
                }
                rig.Step();
            }

            FuzzRound(2000);
            AllocationAssert.NoGcAllocations(() => FuzzRound(2000), "fuzzing allocates");
            Assert.Greater(rig.Server.Counters.DroppedMalformed + rig.Server.Counters.DroppedUnknownConnection, 0);
            Assert.Greater(client.DroppedPackets, 0);
        }

        [Test]
        public void SteadyExchange_UnderLoss_AllocatesNothing()
        {
            var rig = new TransportRig(seed: 4, settings: Unlimited());
            ClientTransport a = rig.AddClient(1, out InMemoryClientSocket sa, out _);
            ClientTransport b = rig.AddClient(2, out InMemoryClientSocket sb, out _);
            Assert.IsTrue(rig.RunUntilConnected());
            var lossy = new NetworkConditions { LatencyMs = 30f, JitterMs = 20f, LossPercent = 5f, DuplicatePercent = 2f };
            sa.Conditions = lossy;
            sb.Conditions = lossy;
            rig.ServerSocket.Conditions = lossy;
            int messages = 0;

            void Exchange(int steps)
            {
                for (int s = 0; s < steps; s++)
                {
                    if (s % 6 == 0)
                    {
                        int length = RecordingHandler.Fill(_message, messages++);
                        a.SendReliable(_message, 0, length);
                        b.SendReliable(_message, 0, length);
                        for (int i = 0; i < rig.Server.Connections.ActiveCount; i++)
                            rig.Server.SendReliable(rig.Server.Connections.ActiveAt(i), _message, 0, length);
                    }
                    if (s % 2 == 0)
                    {
                        a.Send(_payload, _payload.Length, rig.Now);
                        b.Send(_payload, _payload.Length, rig.Now);
                        for (int i = 0; i < rig.Server.Connections.ActiveCount; i++)
                            rig.Server.Send(rig.Server.Connections.ActiveAt(i), _payload, _payload.Length, rig.Now);
                    }
                    rig.Step();
                }
            }

            Exchange(180);
            AllocationAssert.NoGcAllocations(() => Exchange(360), "transport steady state allocates");
            Assert.AreEqual(ClientState.Connected, a.State);
            Assert.AreEqual(ClientState.Connected, b.State);
            Assert.AreEqual(2, rig.Server.Connections.ActiveCount);
        }
    }
}
