using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Diagnostics;
using Arena.Net.Server.Match;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Net.Transport;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Server
{
    /// <summary>
    /// The server side of lag compensation and protection (Server-Spec §2.8, §8, S3.13): the rewind a client asks for is cut to
    /// the limit and to the server's estimate of its delay (counted as suspicious), off means 0, the input log keeps the limited
    /// value so a replay matches; attacks on the protocol (garbage, forged connection ids, replays, floods, inputs out of the
    /// window, malformed messages) are dropped while the match plays on.
    /// </summary>
    public sealed class LagCompensationServerTests
    {
        private static readonly int[] Players = { 0, 3 };

        private static PlayerInput WithDelay(int tick, int salt, byte delay)
        {
            PlayerInput p = MatchServerRig.Wander(tick, salt);
            p.ViewDelayTicks = delay;
            return p;
        }

        private static MatchServerRig StartMatch(ServerSettings settings, byte delay0, byte delay1, int interp0 = 0)
        {
            var rig = new MatchServerRig(ModeKind.Acorns, Players, seed: 13, settings: settings);
            rig.Clients[0].Script = (tick, latest) => WithDelay(tick, 1, delay0);
            rig.Clients[1].Script = (tick, latest) => WithDelay(tick, 5, delay1);
            rig.Clients[0].ReportedInterpDelayTicks = interp0;
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 240, 30f), "the match runs");
            return rig;
        }

        [Test]
        public void InflatedViewDelay_IsCutToTheEstimateAndLimit_HonestOneIsKept_ReplayMatches()
        {
            ServerSettings settings = MatchServerRig.CreateSettings();
            MatchServerRig rig = StartMatch(settings, 200, 3, interp0: 50);
            InputLog log = rig.Room.InputLog;
            int max0 = 0, honest = 0, real1 = 0;
            for (int t = 0; t < log.Ticks; t++)
            {
                int d0 = log.Get(t, Players[0]).ViewDelayTicks;
                if (d0 > max0) max0 = d0;
                PlayerInput p1 = log.Get(t, Players[1]);
                if (p1.ViewDelayTicks == 3) honest++;
                if (p1.Move.X != 0f) real1++;
                Assert.LessOrEqual(p1.ViewDelayTicks, 3, "tick " + t);
            }
            Assert.Greater(max0, 0, "an inflated delay still gets a rewind");
            Assert.LessOrEqual(max0, settings.LagCompMaxRewindTicks, "never above 350 ms");
            Assert.Greater(honest, real1 / 2, "an honest small delay passes unchanged");
            Assert.Greater(rig.Host.Metrics.ViewDelayClamped, 100, "the inflated delay is counted as suspicious");
            Assert.Greater(rig.Host.Metrics.RewoundInputs, 100);
            Assert.IsTrue(ReplayVerifier.Verify(rig.Room.Defs, rig.Room.Setup, log, rig.Room.Simulation.State, out string error), error);
        }

        [Test]
        public void SmallInterpolationReport_LimitsTheRewindToTheEstimate()
        {
            ServerSettings settings = MatchServerRig.CreateSettings();
            MatchServerRig rig = StartMatch(settings, 200, 0, interp0: 0);
            InputLog log = rig.Room.InputLog;
            int max0 = 0;
            for (int t = 0; t < log.Ticks; t++)
                if (log.Get(t, Players[0]).ViewDelayTicks > max0) max0 = log.Get(t, Players[0]).ViewDelayTicks;
            Assert.Greater(max0, 0);
            Assert.Less(max0, settings.LagCompMaxRewindTicks, "without a reported interpolation delay the estimate is below the limit");
        }

        [Test]
        public void LagCompOff_EveryInputHasNoRewind()
        {
            ServerSettings settings = MatchServerRig.CreateSettings();
            settings.LagCompEnabled = false;
            MatchServerRig rig = StartMatch(settings, 200, 3, interp0: 6);
            InputLog log = rig.Room.InputLog;
            for (int t = 0; t < log.Ticks; t++)
            {
                Assert.AreEqual(0, log.Get(t, Players[0]).ViewDelayTicks);
                Assert.AreEqual(0, log.Get(t, Players[1]).ViewDelayTicks);
            }
            Assert.AreEqual(0, rig.Host.Metrics.RewoundInputs);
        }

        [Test]
        public void ProtocolAttacks_AreDropped_MatchPlaysOn()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, Players, seed: 17);
            rig.Clients[0].Script = (tick, latest) => MatchServerRig.Wander(tick, 1);
            rig.Clients[1].Script = (tick, latest) => MatchServerRig.Wander(tick, 5);
            byte[] captured = null;
            int capturedLength = 0;
            NetAddress victim = rig.Sockets[0].Local;
            rig.Network.SendFilter = (from, to, buffer, offset, length) =>
            {
                if (captured == null && from == victim && RawPackets.KindOf(buffer, length) == PacketKind.Data && length > NetProtocol.HeaderBytes + 8)
                {
                    captured = new byte[length];
                    System.Buffer.BlockCopy(buffer, offset, captured, 0, length);
                    capturedLength = length;
                }
                return true;
            };
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 60 && captured != null, 30f));

            ServerTransportCounters c = rig.Host.Transport.Counters;
            ServerMetrics m = rig.Host.Metrics;
            long snapshots0 = rig.Clients[0].SnapshotsDecoded, snapshots1 = rig.Clients[1].SnapshotsDecoded;
            int tickBefore = rig.Room.Tick;
            InMemoryClientSocket attacker = rig.Network.CreateClientSocket(NetAddress.FromIPv4(198, 51, 100, 9, 4321), MatchServerRig.ServerAddress);
            var random = new DeterministicNetRandom(99);
            var junk = new byte[NetLimits.MaxPacketBytes];

            // 1. Garbage of every size, then a flood of it.
            for (int i = 0; i < 600; i++)
            {
                int length = 1 + (int)(random.NextULong() % (ulong)junk.Length);
                for (int k = 0; k < length; k++) junk[k] = (byte)random.NextULong();
                attacker.Send(junk, 0, length);
                if (i % 50 == 0) rig.Step();
            }
            // 2. A forged connection id on a real-looking data packet, many times.
            var forged = new byte[capturedLength];
            System.Buffer.BlockCopy(captured, 0, forged, 0, capturedLength);
            for (int i = 0; i < 300; i++)
            {
                ulong id = random.NextULong();
                for (int k = 0; k < 8; k++) forged[1 + k] = (byte)(id >> (8 * k));
                attacker.Send(forged, 0, capturedLength);
                if (i % 50 == 0) rig.Step();
            }
            // 3. Replays of a real packet from the victim's address and from elsewhere.
            for (int i = 0; i < 20; i++)
            {
                rig.Sockets[0].Send(captured, 0, capturedLength);
                attacker.Send(captured, 0, capturedLength);
            }
            rig.Step();

            // 4. Over the real connection: inputs out of the window, malformed and unknown messages, an oversized delay field.
            ScriptedMatchClient victimClient = rig.Clients[0];
            var message = new InputMessage();
            var writer = new BitWriter();
            var payload = new byte[NetConnection.MaxPayloadBytes];
            for (int i = 0; i < 10; i++)
            {
                message.NewestTick = rig.Room.Tick + 1000 + i;
                message.Count = 1;
                message.Inputs[0] = new PlayerInput(Arena.Core.Math.Vec2.Right, Arena.Core.Math.Vec2.Zero, InputButtons.Attack, 255);
                writer.Reset(payload, 0, payload.Length);
                message.Write(writer);
                victimClient.Transport.Send(payload, writer.BytesWritten, rig.Now);
                rig.Step();
            }
            payload[0] = (byte)MessageKind.Input;
            payload[1] = 0xFF;
            victimClient.Transport.Send(payload, 2, rig.Now); // truncated
            rig.Step();
            payload[0] = 0xEE;
            victimClient.Transport.Send(payload, 16, rig.Now); // unknown kind
            rig.Step();
            writer.Reset(payload, 0, payload.Length);
            writer.WriteByte((byte)MessageKind.Input);
            writer.WriteVarInt(rig.Room.Tick + 2);
            writer.WriteBits(0, NetProtocol.InputCountBits);
            writer.WriteBool(false);
            writer.WriteVarUInt(0);
            writer.WriteSByte(0);
            writer.WriteSByte(0);
            writer.WriteBool(false);
            writer.WriteBits(0, NetProtocol.InputButtonBits);
            writer.WriteVarUInt(100000); // ViewDelayTicks does not fit a byte: garbage
            victimClient.Transport.Send(payload, writer.BytesWritten, rig.Now);

            rig.RunSeconds(5f);

            Assert.Greater(c.DroppedMalformed, 0, "garbage is dropped");
            Assert.Greater(c.DroppedUnknownConnection, 100, "forged connection ids are dropped");
            Assert.GreaterOrEqual(c.DroppedDuplicates, 20, "replays are dropped");
            Assert.Greater(m.TooFarAheadInputs, 0, "inputs beyond the window are dropped");
            Assert.GreaterOrEqual(m.MalformedMessages, 3, "malformed and unknown messages are dropped");
            Assert.AreEqual(2, m.ConnectedPlayers, "both players stay connected");
            Assert.AreEqual(ClientState.Connected, victimClient.Transport.State);
            Assert.Greater(rig.Room.Tick, tickBefore + 120, "the match plays on");
            Assert.Greater(rig.Clients[0].SnapshotsDecoded, snapshots0 + 100);
            Assert.Greater(rig.Clients[1].SnapshotsDecoded, snapshots1 + 100);
            Assert.IsTrue(ReplayVerifier.Verify(rig.Room.Defs, rig.Room.Setup, rig.Room.InputLog, rig.Room.Simulation.State, out string error), error);
        }
    }
}
