using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Transport
{
    /// <summary>
    /// One end of a connection (Server-Spec §3.2): acks and replay window (<see cref="AckTracker"/>), RTT
    /// (<see cref="RttEstimator"/>), the reliable channel, liveness times, per-connection rate limits and address-change state.
    /// Builds and parses <see cref="PacketKind.Data"/> packets; does not own a socket — <see cref="ServerTransport"/> and
    /// <see cref="ClientTransport"/> send what it builds. Data packet body: <c>[reliable section][unreliable payload to the
    /// end]</c>, presence by header flags. Buffers are allocated once (when the slot is first used); the hot path does not
    /// allocate.
    /// </summary>
    public sealed class NetConnection
    {
        private const long MicrosPerMs = 1000L;
        private const long MicrosPerSecond = 1000000L;

        /// <summary>Largest unreliable payload: the packet minus the header.</summary>
        public const int MaxPayloadBytes = NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes;

        private const PacketFlags KnownFlags = PacketFlags.HasReliable | PacketFlags.HasPayload | PacketFlags.HasAcks;

        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();
        private TokenBucket _packetBucket;
        private TokenBucket _byteBucket;
        private long _overLimitSince;
        private long _lastDropMicros;

        public NetConnection(int index)
        {
            Index = index;
            Acks = new AckTracker();
            Rtt = new RttEstimator(0L, 0L, 0L);
            Reliable = new ReliableChannel();
        }

        public int Index { get; }

        public ConnectionState State { get; internal set; }

        public ulong Id { get; internal set; }

        public NetAddress Address { get; internal set; }

        /// <summary>The client's handshake nonce (a repeated <c>ConnectResponse</c> with it gets the same accept).</summary>
        public ulong Nonce { get; internal set; }

        public ConnectPurpose Purpose { get; internal set; }

        public bool HasToken { get; internal set; }

        public NetToken Token { get; internal set; }

        public DisconnectReason CloseReason { get; internal set; }

        public AckTracker Acks { get; }

        public RttEstimator Rtt { get; }

        public ReliableChannel Reliable { get; }

        internal PathValidation Path;

        public long ConnectedAtMicros { get; private set; }
        public long LastReceiveMicros { get; private set; }
        public long LastSendMicros { get; private set; }

        /// <summary>Received reliable messages wait for an acknowledgement since this time (−1 — nothing waits).</summary>
        public long AckPendingSinceMicros { get; private set; }

        public long KeepaliveMicros { get; internal set; }
        public long TimeoutMicros { get; internal set; }

        public long PacketsSent { get; private set; }
        public long PacketsReceived { get; private set; }
        public long PacketsAcked { get; private set; }
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }
        public long DuplicatesDropped { get; private set; }
        public long MalformedDropped { get; private set; }
        public long RateLimitedDropped { get; private set; }

        /// <summary>Starts a fresh connection in this slot (the slot's buffers are reused).</summary>
        internal void Open(ulong id, in NetAddress address, TransportSettings settings, long nowMicros)
        {
            Id = id;
            Address = address;
            State = ConnectionState.Connected;
            CloseReason = DisconnectReason.None;
            Acks.Reset();
            Rtt.Configure(settings.MinRtoMs * MicrosPerMs, settings.MaxRtoMs * MicrosPerMs, settings.InitialRtoMs * MicrosPerMs);
            Rtt.Reset();
            Reliable.Reset();
            Path.Reset();
            KeepaliveMicros = settings.KeepaliveMs * MicrosPerMs;
            TimeoutMicros = settings.DisconnectTimeoutMs * MicrosPerMs;
            ConnectedAtMicros = nowMicros;
            LastReceiveMicros = nowMicros;
            LastSendMicros = nowMicros;
            AckPendingSinceMicros = -1L;
            _packetBucket.Reset();
            _byteBucket.Reset();
            _overLimitSince = -1L;
            _lastDropMicros = 0L;
            PacketsSent = PacketsReceived = PacketsAcked = BytesSent = BytesReceived = 0L;
            DuplicatesDropped = MalformedDropped = RateLimitedDropped = 0L;
        }

        public bool IsConnected => State == ConnectionState.Connected;

        /// <summary>
        /// Builds a data packet into <paramref name="buffer"/> (≥ <see cref="NetLimits.MaxPacketBytes"/>): header, the due
        /// reliable messages that fit next to the payload, then the payload. Returns the packet length.
        /// </summary>
        public int BuildPacket(byte[] buffer, long nowMicros, byte[] payload, int payloadLength)
        {
            if (payloadLength < 0 || payloadLength > MaxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payloadLength));
            var header = new PacketHeader { Kind = PacketKind.Data, ConnectionId = Id };
            header.Sequence = Acks.TakeSequence(nowMicros);
            PacketFlags flags = PacketFlags.None;
            if (Acks.AnyReceived)
            {
                Acks.FillAcks(ref header, nowMicros);
                flags |= PacketFlags.HasAcks;
            }
            _writer.Reset(buffer, NetProtocol.HeaderBytes, NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes);
            int budget = MaxPayloadBytes - payloadLength;
            if (Reliable.Write(_writer, header.Sequence, nowMicros, Rtt.RtoMicros, budget) > 0) flags |= PacketFlags.HasReliable;
            if (payloadLength > 0)
            {
                _writer.WriteBytes(payload, 0, payloadLength);
                flags |= PacketFlags.HasPayload;
            }
            header.Flags = flags;
            header.Write(buffer);
            int length = NetProtocol.HeaderBytes + _writer.BytesWritten;
            LastSendMicros = nowMicros;
            AckPendingSinceMicros = -1L;
            PacketsSent++;
            BytesSent += length;
            return length;
        }

        /// <summary>True if something must go out now: due reliable messages, a pending ack, or the keepalive.</summary>
        public bool NeedsFlush(long nowMicros, long ackDelayMicros) =>
            Reliable.HasDue(nowMicros, Rtt.RtoMicros)
            || (AckPendingSinceMicros >= 0L && nowMicros - AckPendingSinceMicros >= ackDelayMicros)
            || nowMicros - LastSendMicros >= KeepaliveMicros;

        public bool IsTimedOut(long nowMicros) => nowMicros - LastReceiveMicros >= TimeoutMicros;

        /// <summary>
        /// Per-connection incoming limits (§3.2): false — drop the packet silently. <paramref name="longViolation"/> becomes
        /// true when the connection has been dropping for <c>RateLimitDisconnectMs</c> without a quiet second.
        /// </summary>
        public bool AllowIncoming(long nowMicros, int length, TransportSettings settings, out bool longViolation)
        {
            longViolation = false;
            bool ok = _packetBucket.TryTake(nowMicros, settings.PacketsPerSecond, 1)
                && _byteBucket.TryTake(nowMicros, settings.BytesPerSecond, length);
            if (ok)
            {
                if (_overLimitSince >= 0L && nowMicros - _lastDropMicros > MicrosPerSecond) _overLimitSince = -1L;
                return true;
            }
            RateLimitedDropped++;
            if (_overLimitSince < 0L) _overLimitSince = nowMicros;
            _lastDropMicros = nowMicros;
            longViolation = nowMicros - _overLimitSince >= settings.RateLimitDisconnectMs * MicrosPerMs;
            return false;
        }

        /// <summary>
        /// Processes a data packet of this connection (header already parsed). Drops duplicates, replays and malformed
        /// packets without side effects; otherwise applies acks and hands the reliable messages and the payload to
        /// <paramref name="handler"/>. True if the packet was accepted.
        /// </summary>
        public bool ProcessPacket(in PacketHeader header, byte[] packet, int length, long nowMicros, IConnectionHandler handler)
        {
            if ((header.Flags & ~KnownFlags) != 0)
            {
                MalformedDropped++;
                return false;
            }
            if (!Acks.IsNew(header.Sequence))
            {
                DuplicatesDropped++;
                return false;
            }
            bool hasReliable = (header.Flags & PacketFlags.HasReliable) != 0;
            bool hasPayload = (header.Flags & PacketFlags.HasPayload) != 0;
            _reader.Reset(packet, 0, length);
            _reader.SkipBytes(NetProtocol.HeaderBytes);
            if (hasReliable && !ReliableChannel.Validate(_reader))
            {
                MalformedDropped++;
                return false;
            }
            _reader.AlignToByte();
            int payloadStart = _reader.BytesRead;
            int payloadLength = length - payloadStart;
            if (_reader.IsOverflow || payloadLength < 0 || (hasPayload ? payloadLength == 0 : payloadLength != 0))
            {
                MalformedDropped++;
                return false;
            }

            Acks.MarkReceived(header.Sequence, nowMicros);
            LastReceiveMicros = nowMicros;
            PacketsReceived++;
            BytesReceived += length;

            if ((header.Flags & PacketFlags.HasAcks) != 0) ProcessAcks(header, nowMicros, handler);
            if (hasReliable)
            {
                if (AckPendingSinceMicros < 0L) AckPendingSinceMicros = nowMicros;
                _reader.Reset(packet, 0, length);
                _reader.SkipBytes(NetProtocol.HeaderBytes);
                Reliable.Receive(_reader, packet, handler, Index);
            }
            if (hasPayload && State == ConnectionState.Connected)
                handler?.OnPayload(Index, header.Sequence, packet, payloadStart, payloadLength);
            return true;
        }

        /// <summary>Any valid packet from the peer (disconnect, path response) proves it is alive.</summary>
        internal void Touch(long nowMicros) => LastReceiveMicros = nowMicros;

        private void ProcessAcks(in PacketHeader header, long nowMicros, IConnectionHandler handler)
        {
            for (int i = 0; i <= AckTracker.AckBitsCount; i++)
            {
                if (i > 0 && (header.AckBits & (1u << (i - 1))) == 0u) continue;
                ushort seq = (ushort)(header.Ack - i);
                if (!Acks.TryAcknowledge(seq, out long sentAt)) continue;
                PacketsAcked++;
                if (i == 0) Rtt.AddSample(nowMicros - sentAt - (long)header.AckDelay * NetProtocol.AckDelayUnitMicros);
                Reliable.OnPacketAcked(seq);
                handler?.OnPacketAcked(Index, seq);
            }
        }
    }
}
