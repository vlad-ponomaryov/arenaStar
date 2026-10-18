using Arena.Core.Modes;
using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Lobby
{
    /// <summary>Lobby messages (Server-Spec §4.2, S3.8): round trip, kind byte first, truncated bodies are rejected without exceptions.</summary>
    public sealed class LobbyMessagesTests
    {
        private readonly byte[] _buffer = new byte[NetLimits.MaxReliableMessageBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();

        [Test]
        public void QueueJoin_RoundTrips()
        {
            int n = Write(w => new QueueJoinMessage { Mode = ModeKind.LastBeast, FighterIndex = 5 }.Write(w), MessageKind.QueueJoin);
            var m = new QueueJoinMessage();
            Assert.IsTrue(m.Read(Body(n)));
            Assert.AreEqual(ModeKind.LastBeast, m.Mode);
            Assert.AreEqual(5, m.FighterIndex);
            Assert.IsFalse(new QueueJoinMessage().Read(Body(1)), "truncated");
        }

        [Test]
        public void QueueStatus_RoundTrips_AndChecksRanges()
        {
            var sent = new QueueStatusMessage { State = QueueState.Searching, Mode = ModeKind.Acorns, WaitSeconds = 14, Found = 3, Needed = 6 };
            int n = Write(w => sent.Write(w), MessageKind.QueueStatus);
            var m = new QueueStatusMessage();
            Assert.IsTrue(m.Read(Body(n)));
            Assert.AreEqual(QueueState.Searching, m.State);
            Assert.AreEqual(ModeKind.Acorns, m.Mode);
            Assert.AreEqual(14, m.WaitSeconds);
            Assert.AreEqual(3, m.Found);
            Assert.AreEqual(6, m.Needed);
            n = Write(w => new QueueStatusMessage { Found = 7, Needed = 6 }.Write(w), MessageKind.QueueStatus);
            Assert.IsFalse(new QueueStatusMessage().Read(Body(n)), "found > needed");
            Assert.IsFalse(new QueueStatusMessage().Read(Body(3)), "truncated");
        }

        [Test]
        public void MatchAssigned_RoundTrips_WithAndWithoutAddress()
        {
            var sent = new MatchAssignedMessage { MatchId = 0x1234_5678_9ABCul, RejoinToken = new NetToken(7ul, 9ul), Slot = 9 };
            int n = Write(w => sent.Write(w), MessageKind.MatchAssigned);
            var m = new MatchAssignedMessage();
            Assert.IsTrue(m.Read(Body(n)));
            Assert.AreEqual(sent.MatchId, m.MatchId);
            Assert.IsTrue(m.Address.IsEmpty);
            Assert.IsTrue(m.RejoinToken.Equals(sent.RejoinToken));
            Assert.AreEqual(9, m.Slot);

            sent.Address = NetAddress.FromIPv4(192, 168, 1, 5, 7777);
            n = Write(w => sent.Write(w), MessageKind.MatchAssigned);
            Assert.IsTrue(m.Read(Body(n)));
            Assert.AreEqual(sent.Address, m.Address);
            Assert.IsFalse(new MatchAssignedMessage().Read(Body(n - 3)), "truncated");

            n = Write(w => new MatchAssignedMessage { MatchId = 1ul, Slot = 0 }.Write(w), MessageKind.MatchAssigned);
            Assert.IsFalse(new MatchAssignedMessage().Read(Body(n)), "an empty token is invalid");
        }

        [Test]
        public void QueueRejected_And_ServerNotice_RoundTrip()
        {
            int n = Write(w => new QueueRejectedMessage { Reason = QueueRejectReason.InvalidFighter }.Write(w), MessageKind.QueueRejected);
            var r = new QueueRejectedMessage();
            Assert.IsTrue(r.Read(Body(n)));
            Assert.AreEqual(QueueRejectReason.InvalidFighter, r.Reason);
            n = Write(w => new QueueRejectedMessage().Write(w), MessageKind.QueueRejected);
            Assert.IsFalse(new QueueRejectedMessage().Read(Body(n)), "reason None is invalid");

            n = Write(w => new ServerNoticeMessage { Kind = ServerNoticeKind.Shutdown, Seconds = 180 }.Write(w), MessageKind.ServerNotice);
            var s = new ServerNoticeMessage();
            Assert.IsTrue(s.Read(Body(n)));
            Assert.AreEqual(ServerNoticeKind.Shutdown, s.Kind);
            Assert.AreEqual(180, s.Seconds);
            Assert.IsFalse(new ServerNoticeMessage().Read(Body(1)), "truncated");
        }

        private delegate void WriteAction(BitWriter writer);

        private int Write(WriteAction write, MessageKind kind)
        {
            _writer.Reset(_buffer, 0, _buffer.Length);
            write(_writer);
            Assert.IsFalse(_writer.IsOverflow);
            Assert.AreEqual((byte)kind, _buffer[0], "kind byte first");
            return _writer.BytesWritten;
        }

        /// <summary>The body after the kind byte, <paramref name="total"/> bytes of the message in all.</summary>
        private BitReader Body(int total)
        {
            _reader.Reset(_buffer, 1, total - 1);
            return _reader;
        }
    }
}
