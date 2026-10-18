using System;
using Arena.Net.Serialization;

namespace Arena.Net.Transport
{
    /// <summary>
    /// Reliable ordered messages of one connection (Server-Spec §3.2): no fragmentation (each message ≤
    /// <see cref="NetLimits.MaxReliableMessageBytes"/>), up to <see cref="NetLimits.ReliableWindow"/> messages in flight, stored in
    /// a byte ring of <see cref="NetLimits.ReliableBufferBytes"/>. Messages ride on any outgoing packet and are repeated after
    /// the RTO until a packet carrying them is acknowledged.
    /// <para>
    /// The receiver mirrors the sender's ring: each message carries its ring offset and an out-of-order message is stored at the
    /// same offset. The sender frees ring space strictly in message order, only after acknowledgement, so space it reuses always
    /// held messages the receiver has already delivered — buffering never fails and needs no more memory than the sender's ring.
    /// </para>
    /// Section format: <c>varuint count; count × (u16 id, u16 ringOffset, varuint length, bytes)</c>, byte-aligned.
    /// </summary>
    public sealed class ReliableChannel
    {
        private const int RingBytes = NetLimits.ReliableBufferBytes;
        private const int Window = NetLimits.ReliableWindow;
        private const int MessageFixedBytes = 4; // u16 id + u16 offset

        // ---- send side ----
        private readonly byte[] _sendRing = new byte[RingBytes];
        private readonly int[] _sendOffset = new int[Window];
        private readonly int[] _sendLength = new int[Window];
        private readonly long[] _sendLastTime = new long[Window];
        private readonly int[] _sendTimes = new int[Window];
        private readonly ushort[] _sendSeqA = new ushort[Window];
        private readonly ushort[] _sendSeqB = new ushort[Window];
        private readonly bool[] _sendAcked = new bool[Window];
        private readonly int[] _selected = new int[Window];
        private int _sendHead;
        private int _sendTail;
        private int _sendCount;
        private ushort _oldestId;

        // ---- receive side ----
        private readonly byte[] _recvRing = new byte[RingBytes];
        private readonly bool[] _recvPresent = new bool[Window];
        private readonly int[] _recvOffset = new int[Window];
        private readonly int[] _recvLength = new int[Window];
        private ushort _expectedId;

        /// <summary>Messages queued and not yet acknowledged.</summary>
        public int PendingCount => _sendCount;

        public long SentMessages { get; private set; }
        public long RetransmittedMessages { get; private set; }
        public long DeliveredMessages { get; private set; }

        public void Reset()
        {
            _sendHead = 0;
            _sendTail = 0;
            _sendCount = 0;
            _oldestId = 0;
            _expectedId = 0;
            Array.Clear(_sendAcked, 0, Window);
            Array.Clear(_recvPresent, 0, Window);
            SentMessages = 0;
            RetransmittedMessages = 0;
            DeliveredMessages = 0;
        }

        /// <summary>
        /// Queues a message. Throws if it is empty or longer than <see cref="NetLimits.MaxReliableMessageBytes"/> (a coding
        /// error: there is no fragmentation, §3.2). False if the window or the ring is full.
        /// </summary>
        public bool Enqueue(byte[] source, int offset, int length)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (length <= 0 || length > NetLimits.MaxReliableMessageBytes)
                throw new ArgumentOutOfRangeException(nameof(length), "reliable message must be 1.." + NetLimits.MaxReliableMessageBytes + " bytes");
            if (_sendCount == Window) return false;
            if (!TryPlace(length, out int at)) return false;
            Buffer.BlockCopy(source, offset, _sendRing, at, length);
            int index = (ushort)(_oldestId + _sendCount) % Window;
            _sendOffset[index] = at;
            _sendLength[index] = length;
            _sendLastTime[index] = 0L;
            _sendTimes[index] = 0;
            _sendAcked[index] = false;
            _sendCount++;
            _sendTail = at + length;
            return true;
        }

        private bool TryPlace(int length, out int at)
        {
            if (_sendCount == 0)
            {
                _sendHead = 0;
                _sendTail = 0;
            }
            if (_sendCount == 0 || _sendTail > _sendHead)
            {
                // Free: [tail, end) and [0, head).
                if (_sendTail + length <= RingBytes)
                {
                    at = _sendTail;
                    return true;
                }
                if (length <= _sendHead)
                {
                    at = 0;
                    return true;
                }
                at = 0;
                return false;
            }
            // Wrapped: free is [tail, head).
            at = _sendTail;
            return _sendTail + length <= _sendHead;
        }

        /// <summary>True if a message was never sent or its RTO has expired.</summary>
        public bool HasDue(long nowMicros, long rtoMicros)
        {
            for (int k = 0; k < _sendCount; k++)
            {
                int index = (ushort)(_oldestId + k) % Window;
                if (_sendAcked[index]) continue;
                if (_sendTimes[index] == 0 || nowMicros - _sendLastTime[index] >= rtoMicros) return true;
            }
            return false;
        }

        /// <summary>
        /// Writes the due messages that fit into <paramref name="budgetBytes"/> as a reliable section and remembers that they
        /// went in packet <paramref name="packetSequence"/>. Returns the number of messages; 0 — nothing was written.
        /// </summary>
        public int Write(BitWriter writer, ushort packetSequence, long nowMicros, long rtoMicros, int budgetBytes)
        {
            int count = 0;
            int bytes = 1; // count (≤ 64 → one varint byte)
            for (int k = 0; k < _sendCount; k++)
            {
                int index = (ushort)(_oldestId + k) % Window;
                if (_sendAcked[index]) continue;
                if (_sendTimes[index] > 0 && nowMicros - _sendLastTime[index] < rtoMicros) continue;
                int length = _sendLength[index];
                int cost = MessageFixedBytes + VarInt.ByteCount((uint)length) + length;
                if (bytes + cost > budgetBytes) continue;
                _selected[count++] = k;
                bytes += cost;
            }
            if (count == 0) return 0;
            writer.WriteVarUInt((uint)count);
            for (int i = 0; i < count; i++)
            {
                ushort id = (ushort)(_oldestId + _selected[i]);
                int index = id % Window;
                writer.WriteUInt16(id);
                writer.WriteUInt16((ushort)_sendOffset[index]);
                writer.WriteVarUInt((uint)_sendLength[index]);
                writer.WriteBytes(_sendRing, _sendOffset[index], _sendLength[index]);
                if (_sendTimes[index] > 0) RetransmittedMessages++;
                else SentMessages++;
                _sendTimes[index]++;
                _sendLastTime[index] = nowMicros;
                _sendSeqB[index] = _sendSeqA[index];
                _sendSeqA[index] = packetSequence;
            }
            return count;
        }

        /// <summary>A packet was acknowledged: the messages it carried (in one of their last two sends) are delivered.</summary>
        public void OnPacketAcked(ushort packetSequence)
        {
            for (int k = 0; k < _sendCount; k++)
            {
                int index = (ushort)(_oldestId + k) % Window;
                if (_sendAcked[index] || _sendTimes[index] == 0) continue;
                if (_sendSeqA[index] == packetSequence || (_sendTimes[index] > 1 && _sendSeqB[index] == packetSequence))
                    _sendAcked[index] = true;
            }
            while (_sendCount > 0 && _sendAcked[_oldestId % Window])
            {
                _sendAcked[_oldestId % Window] = false;
                _oldestId++;
                _sendCount--;
                _sendHead = _sendCount > 0 ? _sendOffset[_oldestId % Window] : _sendTail;
            }
        }

        /// <summary>Checks a reliable section without side effects (untrusted input). The reader advances past it.</summary>
        public static bool Validate(BitReader reader)
        {
            uint count = reader.ReadVarUInt();
            if (reader.IsOverflow || count == 0u || count > Window) return false;
            for (uint i = 0; i < count; i++)
            {
                reader.ReadUInt16();
                int offset = reader.ReadUInt16();
                uint length = reader.ReadVarUInt();
                if (reader.IsOverflow || length == 0u || length > NetLimits.MaxReliableMessageBytes || offset + (int)length > RingBytes)
                    return false;
                reader.SkipBytes((int)length);
                if (reader.IsOverflow) return false;
            }
            return true;
        }

        /// <summary>
        /// Processes a section already checked by <see cref="Validate"/>: delivers new messages in order to
        /// <paramref name="handler"/>, stores early ones, ignores duplicates. The reader must start at offset 0 of
        /// <paramref name="packet"/>.
        /// </summary>
        public void Receive(BitReader reader, byte[] packet, IConnectionHandler handler, int connection)
        {
            uint count = reader.ReadVarUInt();
            for (uint i = 0; i < count; i++)
            {
                ushort id = reader.ReadUInt16();
                int offset = reader.ReadUInt16();
                int length = (int)reader.ReadVarUInt();
                reader.AlignToByte();
                int at = reader.BytesRead;
                reader.SkipBytes(length);
                int ahead = Sequence16.Distance(id, _expectedId);
                if (ahead >= Window) continue; // already delivered (or a peer outside the protocol)
                if (ahead == 0)
                {
                    _expectedId++;
                    DeliveredMessages++;
                    handler?.OnReliableMessage(connection, packet, at, length);
                    DeliverBuffered(handler, connection);
                    continue;
                }
                int index = id % Window;
                if (_recvPresent[index]) continue;
                Buffer.BlockCopy(packet, at, _recvRing, offset, length);
                _recvPresent[index] = true;
                _recvOffset[index] = offset;
                _recvLength[index] = length;
            }
        }

        private void DeliverBuffered(IConnectionHandler handler, int connection)
        {
            while (_recvPresent[_expectedId % Window])
            {
                int index = _expectedId % Window;
                _recvPresent[index] = false;
                _expectedId++;
                DeliveredMessages++;
                handler?.OnReliableMessage(connection, _recvRing, _recvOffset[index], _recvLength[index]);
            }
        }
    }
}
