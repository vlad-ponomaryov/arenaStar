using Arena.Net.Protocol;

namespace Arena.Net.Transport
{
    /// <summary>
    /// Packet acknowledgements after Glenn Fiedler (Server-Spec §3.2): each packet carries the newest received sequence
    /// (<c>ack</c>) and 32 bits for the ones before it. Remembers what we sent (sequence and time, ring of
    /// <see cref="NetLimits.SentPacketHistory"/>) to learn which packets arrived and to sample RTT; tracks what we received
    /// for our own acks and as the replay window — a sequence seen before, or older than 32 behind the newest, is rejected.
    /// No allocations after construction.
    /// </summary>
    public sealed class AckTracker
    {
        public const int AckBitsCount = 32;

        private readonly ushort[] _sentSequence = new ushort[NetLimits.SentPacketHistory];
        private readonly long[] _sentTime = new long[NetLimits.SentPacketHistory];
        private readonly bool[] _sentValid = new bool[NetLimits.SentPacketHistory];
        private readonly bool[] _sentAcked = new bool[NetLimits.SentPacketHistory];

        private ushort _nextSequence;
        private bool _anySent;

        private bool _anyReceived;
        private ushort _remoteSequence;
        private uint _remoteBits;
        private long _remoteReceivedAt;

        public ushort NextSequence => _nextSequence;

        public bool AnySent => _anySent;

        /// <summary>At least one packet was received: the ack fields of outgoing headers are valid (<c>PacketFlags.HasAcks</c>).</summary>
        public bool AnyReceived => _anyReceived;

        public void Reset()
        {
            System.Array.Clear(_sentValid, 0, _sentValid.Length);
            System.Array.Clear(_sentAcked, 0, _sentAcked.Length);
            _nextSequence = 0;
            _anySent = false;
            _anyReceived = false;
            _remoteSequence = 0;
            _remoteBits = 0u;
            _remoteReceivedAt = 0L;
        }

        /// <summary>Takes the next sequence for an outgoing packet and remembers its send time.</summary>
        public ushort TakeSequence(long nowMicros)
        {
            ushort seq = _nextSequence++;
            int slot = seq % NetLimits.SentPacketHistory;
            _sentSequence[slot] = seq;
            _sentTime[slot] = nowMicros;
            _sentValid[slot] = true;
            _sentAcked[slot] = false;
            _anySent = true;
            return seq;
        }

        /// <summary>Fills <c>ack</c>, <c>ackBits</c> and <c>ackDelay</c> of an outgoing header.</summary>
        public void FillAcks(ref PacketHeader header, long nowMicros)
        {
            if (!_anyReceived)
            {
                header.Ack = 0;
                header.AckBits = 0u;
                header.AckDelay = 0;
                return;
            }
            header.Ack = _remoteSequence;
            header.AckBits = _remoteBits;
            long delay = (nowMicros - _remoteReceivedAt) / NetProtocol.AckDelayUnitMicros;
            header.AckDelay = (byte)(delay < 0 ? 0 : delay > byte.MaxValue ? byte.MaxValue : delay);
        }

        /// <summary>True if an incoming sequence was not seen yet and is inside the window (replay protection).</summary>
        public bool IsNew(ushort sequence)
        {
            if (!_anyReceived) return true;
            if (sequence == _remoteSequence) return false;
            if (Sequence16.IsNewer(sequence, _remoteSequence)) return true;
            int back = Sequence16.Distance(_remoteSequence, sequence);
            if (back > AckBitsCount) return false;
            return (_remoteBits & (1u << (back - 1))) == 0u;
        }

        /// <summary>Records an accepted incoming sequence (call after <see cref="IsNew"/> and a successful parse).</summary>
        public void MarkReceived(ushort sequence, long nowMicros)
        {
            if (!_anyReceived)
            {
                _anyReceived = true;
                _remoteSequence = sequence;
                _remoteBits = 0u;
                _remoteReceivedAt = nowMicros;
                return;
            }
            if (Sequence16.IsNewer(sequence, _remoteSequence))
            {
                int shift = Sequence16.Distance(sequence, _remoteSequence);
                _remoteBits = shift > AckBitsCount ? 0u : shift == AckBitsCount ? 1u << (AckBitsCount - 1) : (_remoteBits << shift) | (1u << (shift - 1));
                _remoteSequence = sequence;
                _remoteReceivedAt = nowMicros;
                return;
            }
            int back = Sequence16.Distance(_remoteSequence, sequence);
            if (back >= 1 && back <= AckBitsCount) _remoteBits |= 1u << (back - 1);
        }

        /// <summary>
        /// Marks one of our packets acknowledged. True only the first time for a packet we really sent and still remember;
        /// <paramref name="sentAtMicros"/> is its send time.
        /// </summary>
        public bool TryAcknowledge(ushort sequence, out long sentAtMicros)
        {
            sentAtMicros = 0L;
            if (!_anySent) return false;
            ushort newest = (ushort)(_nextSequence - 1);
            if (sequence != newest && Sequence16.IsNewer(sequence, newest)) return false;
            int slot = sequence % NetLimits.SentPacketHistory;
            if (!_sentValid[slot] || _sentSequence[slot] != sequence || _sentAcked[slot]) return false;
            _sentAcked[slot] = true;
            sentAtMicros = _sentTime[slot];
            return true;
        }
    }
}
