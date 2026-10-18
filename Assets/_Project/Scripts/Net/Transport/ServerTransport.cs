using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Transport
{
    /// <summary>
    /// The server end of the transport (Server-Spec §3.2, §3.3): one datagram socket, stateless cookie handshake, connections
    /// by random 64-bit id, replay window, reliable channel, keepalive and timeout, path validation on address change,
    /// per-IP and per-connection limits, pings and LAN discovery. Single-threaded (the game thread, §5.3): <see cref="Receive"/>
    /// drains the socket, <see cref="Update"/> does timers and flushes. No allocations in steady state (a connection slot
    /// allocates its buffers the first time it is used).
    /// <para>Before the address is proven (valid cookie) the server stores nothing and never answers with more bytes than it
    /// got: requests are padded to <see cref="NetProtocol.HandshakePacketBytes"/>, pings and discovery are answered with
    /// packets of the request's size.</para>
    /// </summary>
    public sealed class ServerTransport
    {
        private const long MicrosPerMs = 1000L;

        private readonly IServerDatagramSocket _socket;
        private readonly TransportSettings _settings;
        private readonly ulong _contentHash;
        private readonly INetRandom _random;
        private readonly IServerTransportHandler _handler;
        private readonly ConnectionTable _table;
        private readonly Handshake _handshake;
        private readonly RateLimiter _ipLimiter = new RateLimiter();
        private readonly byte[] _recv = new byte[NetLimits.ReceiveBufferBytes];
        private readonly byte[] _send = new byte[NetLimits.MaxPacketBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();
        private readonly NetConnection[] _closing;
        private int _closingCount;
        private bool _processing;

        public ServerTransport(IServerDatagramSocket socket, TransportSettings settings, ulong contentHash, INetRandom random,
            IServerTransportHandler handler)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _handler = handler;
            _contentHash = contentHash;
            _table = new ConnectionTable(settings.MaxConnections);
            _handshake = new Handshake(random);
            _closing = new NetConnection[settings.MaxConnections];
        }

        public ServerTransportCounters Counters { get; } = new ServerTransportCounters();

        public ConnectionTable Connections => _table;

        public TransportSettings Settings => _settings;

        /// <summary>Shown in <c>DiscoverReply</c>; the lobby refuses new players itself (S3.8).</summary>
        public bool Maintenance { get; set; }

        public long RateLimitedHandshakes => _ipLimiter.RejectedCount;

        public NetConnection GetConnection(int index) => _table.Get(index);

        /// <summary>Takes every pending datagram from the socket and processes it.</summary>
        public void Receive(long nowMicros)
        {
            _processing = true;
            try
            {
                while (_socket.TryReceive(_recv, out int length, out NetAddress from))
                {
                    Counters.PacketsReceived++;
                    Counters.BytesReceived += length;
                    HandleDatagram(length, from, nowMicros);
                }
            }
            finally
            {
                _processing = false;
            }
            FlushClosed();
        }

        /// <summary>Timeouts, path challenges, retransmissions, acknowledgements and keepalives of all connections.</summary>
        public void Update(long nowMicros)
        {
            _processing = true;
            try
            {
                long ackDelay = _settings.AckDelayMs * MicrosPerMs;
                long pathRetry = _settings.PathChallengeRetryMs * MicrosPerMs;
                for (int i = _table.ActiveCount - 1; i >= 0; i--)
                {
                    NetConnection c = _table.Get(_table.ActiveAt(i));
                    if (c.State != ConnectionState.Connected) continue;
                    if (c.IsTimedOut(nowMicros))
                    {
                        Counters.Timeouts++;
                        Close(c, DisconnectReason.Timeout, false);
                        continue;
                    }
                    if (c.Path.IsDue(nowMicros, pathRetry)) SendPathChallenge(c, nowMicros);
                    if (c.NeedsFlush(nowMicros, ackDelay)) SendData(c, null, 0, nowMicros);
                }
            }
            finally
            {
                _processing = false;
            }
            FlushClosed();
        }

        /// <summary>Sends a data packet with <paramref name="payload"/> (may be empty) and the due reliable messages.</summary>
        public bool Send(int connection, byte[] payload, int payloadLength, long nowMicros)
        {
            NetConnection c = _table.Get(connection);
            return c != null && c.State == ConnectionState.Connected && SendData(c, payload, payloadLength, nowMicros);
        }

        /// <summary>
        /// Queues a reliable message; it goes out with the next packet. False if the connection is not open or its reliable
        /// window / ring is full — the application decides: retry later or close with
        /// <see cref="DisconnectReason.ReliableOverflow"/>.
        /// </summary>
        public bool SendReliable(int connection, byte[] message, int offset, int length)
        {
            NetConnection c = _table.Get(connection);
            return c != null && c.State == ConnectionState.Connected && c.Reliable.Enqueue(message, offset, length);
        }

        /// <summary>Closes a connection: <c>Disconnect</c> goes out <c>DisconnectRepeats</c> times, the handler is told.</summary>
        public void Disconnect(int connection, DisconnectReason reason)
        {
            NetConnection c = _table.Get(connection);
            if (c == null || c.State != ConnectionState.Connected) return;
            Close(c, reason, true);
        }

        private void HandleDatagram(int length, in NetAddress from, long now)
        {
            if (length > NetLimits.MaxPacketBytes)
            {
                Counters.DroppedOversize++;
                return;
            }
            if (!PacketHeader.TryRead(_recv, length, out PacketHeader header))
            {
                Counters.DroppedMalformed++;
                return;
            }
            switch (header.Kind)
            {
                case PacketKind.Data:
                    OnData(header, length, from, now);
                    break;
                case PacketKind.ConnectRequest:
                    OnConnectRequest(header, length, from, now);
                    break;
                case PacketKind.ConnectResponse:
                    OnConnectResponse(header, length, from, now);
                    break;
                case PacketKind.Ping:
                    OnPing(header, length, from, now);
                    break;
                case PacketKind.Discover:
                    OnDiscover(header, length, from, now);
                    break;
                case PacketKind.Disconnect:
                    OnPeerDisconnect(header, length, from);
                    break;
                case PacketKind.PathResponse:
                    OnPathResponse(header, length, from, now);
                    break;
                default:
                    Counters.DroppedMalformed++;
                    break;
            }
        }

        private void OnData(in PacketHeader header, int length, in NetAddress from, long now)
        {
            NetConnection c = _table.FindById(header.ConnectionId);
            if (c == null || c.State != ConnectionState.Connected)
            {
                Counters.DroppedUnknownConnection++;
                return;
            }
            if (!c.AllowIncoming(now, length, _settings, out bool longViolation))
            {
                Counters.DroppedRateLimited++;
                if (longViolation)
                {
                    Counters.RateLimitDisconnects++;
                    Close(c, DisconnectReason.RateLimited, true);
                }
                return;
            }
            long duplicates = c.DuplicatesDropped;
            if (!c.ProcessPacket(header, _recv, length, now, _handler))
            {
                if (c.DuplicatesDropped != duplicates) Counters.DroppedDuplicates++;
                else Counters.DroppedMalformed++;
                return;
            }
            // A fresh packet from another address: prove the new path before moving the connection there (§3.2).
            if (from != c.Address && c.State == ConnectionState.Connected && (!c.Path.Pending || c.Path.Candidate != from))
            {
                c.Path.Start(from, _random.NextULong());
                SendPathChallenge(c, now);
            }
        }

        private void OnConnectRequest(in PacketHeader header, int length, in NetAddress from, long now)
        {
            if (length != NetProtocol.HandshakePacketBytes || !IsConnectionless(header))
            {
                Counters.DroppedMalformed++;
                return;
            }
            if (!_ipLimiter.Allow(from, now, _settings.HandshakesPerIpPerMinute))
            {
                Counters.DroppedRateLimited++;
                return;
            }
            _reader.Reset(_recv, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!ConnectRequestMessage.Read(_reader, out ConnectRequestMessage request) || request.ProtocolId != NetProtocol.Id)
            {
                Counters.DroppedMalformed++;
                return;
            }
            RejectReason versions = CheckVersions(request.ProtocolVersion, request.ContentHash);
            if (versions != RejectReason.None)
            {
                SendRejected(from, request.Nonce, versions);
                return;
            }
            var challenge = new ConnectChallengeMessage
            {
                Nonce = request.Nonce,
                Cookie = _handshake.CurrentCookie(from, request.Nonce, now, _settings.CookieSliceMs),
            };
            BeginBody();
            challenge.Write(_writer);
            Counters.Challenges++;
            FinishAndSend(PacketHeader.Connectionless(PacketKind.ConnectChallenge), 0, from);
        }

        private void OnConnectResponse(in PacketHeader header, int length, in NetAddress from, long now)
        {
            if (length != NetProtocol.HandshakePacketBytes || !IsConnectionless(header))
            {
                Counters.DroppedMalformed++;
                return;
            }
            if (!_ipLimiter.Allow(from, now, _settings.HandshakesPerIpPerMinute))
            {
                Counters.DroppedRateLimited++;
                return;
            }
            _reader.Reset(_recv, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!ConnectResponseMessage.Read(_reader, out ConnectResponseMessage response) || response.ProtocolId != NetProtocol.Id)
            {
                Counters.DroppedMalformed++;
                return;
            }
            if (!_handshake.Verify(from, response.Nonce, response.Cookie, now, _settings.CookieSliceMs))
            {
                Counters.BadCookies++;
                return;
            }
            RejectReason versions = CheckVersions(response.ProtocolVersion, response.ContentHash);
            if (versions != RejectReason.None)
            {
                SendRejected(from, response.Nonce, versions);
                return;
            }
            NetConnection existing = _table.FindByAddress(from);
            if (existing != null)
            {
                if (existing.Nonce == response.Nonce)
                {
                    // Our ConnectAccepted was lost: the same answer again, no second connection (§3.2).
                    Counters.AcceptRepeats++;
                    SendAccepted(existing);
                    return;
                }
                // The client restarted from the same address and port: the old connection is dead.
                Close(existing, DisconnectReason.Closed, false);
            }
            RejectReason approval = _handler != null ? _handler.Approve(response.Purpose, response.HasToken, response.Token) : RejectReason.None;
            if (approval != RejectReason.None)
            {
                SendRejected(from, response.Nonce, approval);
                return;
            }
            NetConnection c = _table.Allocate(NewConnectionId());
            if (c == null)
            {
                SendRejected(from, response.Nonce, RejectReason.ServerFull);
                return;
            }
            c.Open(c.Id, from, _settings, now);
            c.Nonce = response.Nonce;
            c.Purpose = response.Purpose;
            c.HasToken = response.HasToken;
            c.Token = response.Token;
            Counters.Accepted++;
            SendAccepted(c);
            _handler?.OnConnected(c.Index);
        }

        private void OnPing(in PacketHeader header, int length, in NetAddress from, long now)
        {
            if (length != NetProtocol.PingPacketBytes || !IsConnectionless(header)) return;
            if (!_ipLimiter.Allow(from, now, _settings.HandshakesPerIpPerMinute))
            {
                Counters.DroppedRateLimited++;
                return;
            }
            _reader.Reset(_recv, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!PingMessage.Read(_reader, out PingMessage ping) || ping.ProtocolId != NetProtocol.Id) return;
            BeginBody();
            ping.Write(_writer);
            Counters.Pongs++;
            FinishAndSend(PacketHeader.Connectionless(PacketKind.Pong), NetProtocol.PingPacketBytes, from);
        }

        private void OnDiscover(in PacketHeader header, int length, in NetAddress from, long now)
        {
            if (!_settings.LanDiscovery || !from.IsPrivate) return;
            if (length != NetProtocol.DiscoverPacketBytes || !IsConnectionless(header)) return;
            if (!_ipLimiter.Allow(from, now, _settings.HandshakesPerIpPerMinute))
            {
                Counters.DroppedRateLimited++;
                return;
            }
            _reader.Reset(_recv, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!DiscoverMessage.Read(_reader, out DiscoverMessage discover) || discover.ProtocolId != NetProtocol.Id) return;
            var reply = new DiscoverReplyMessage
            {
                ProtocolId = NetProtocol.Id,
                Nonce = discover.Nonce,
                ProtocolVersion = NetProtocol.Version,
                ContentHash = _contentHash,
                ServerNameKey = _settings.ServerNameKey,
                Connections = (ushort)Math.Min(_table.ActiveCount, ushort.MaxValue),
                Capacity = (ushort)Math.Min(_table.Capacity, ushort.MaxValue),
                Maintenance = Maintenance,
            };
            BeginBody();
            reply.Write(_writer);
            Counters.DiscoverReplies++;
            FinishAndSend(PacketHeader.Connectionless(PacketKind.DiscoverReply), NetProtocol.DiscoverPacketBytes, from);
        }

        private void OnPeerDisconnect(in PacketHeader header, int length, in NetAddress from)
        {
            if (length < NetProtocol.DisconnectPacketBytes) return;
            NetConnection c = _table.FindById(header.ConnectionId);
            if (c == null || c.State != ConnectionState.Connected || c.Address != from) return;
            Close(c, DisconnectReason.Closed, false);
        }

        private void OnPathResponse(in PacketHeader header, int length, in NetAddress from, long now)
        {
            if (length < NetProtocol.PathPacketBytes) return;
            NetConnection c = _table.FindById(header.ConnectionId);
            if (c == null || c.State != ConnectionState.Connected || !c.Path.Pending) return;
            if (c.Path.Candidate != from || c.Path.Challenge != header.PathData) return;
            c.Address = from;
            c.Path.Reset();
            c.Touch(now);
            Counters.PathChanges++;
        }

        private RejectReason CheckVersions(ushort protocolVersion, ulong contentHash)
        {
            if (protocolVersion != NetProtocol.Version) return RejectReason.VersionMismatch;
            if (contentHash != _contentHash) return RejectReason.ContentMismatch;
            return RejectReason.None;
        }

        private static bool IsConnectionless(in PacketHeader header) => header.ConnectionId == 0ul && header.Flags == PacketFlags.None;

        private ulong NewConnectionId()
        {
            while (true)
            {
                ulong id = _random.NextULong();
                if (id != 0ul && !_table.ContainsId(id)) return id;
            }
        }

        private bool SendData(NetConnection c, byte[] payload, int payloadLength, long now)
        {
            int length = c.BuildPacket(_send, now, payload, payloadLength);
            return SendRaw(length, c.Address);
        }

        private void SendAccepted(NetConnection c)
        {
            var accepted = new ConnectAcceptedMessage
            {
                Nonce = c.Nonce,
                KeepaliveMs = (uint)_settings.KeepaliveMs,
                DisconnectTimeoutMs = (uint)_settings.DisconnectTimeoutMs,
                PacketsPerSecond = (uint)_settings.PacketsPerSecond,
                BytesPerSecond = (uint)_settings.BytesPerSecond,
            };
            BeginBody();
            accepted.Write(_writer);
            var header = new PacketHeader { Kind = PacketKind.ConnectAccepted, ConnectionId = c.Id };
            FinishAndSend(header, 0, c.Address);
        }

        private void SendRejected(in NetAddress to, ulong nonce, RejectReason reason)
        {
            var rejected = new ConnectRejectedMessage { Nonce = nonce, Reason = reason };
            BeginBody();
            rejected.Write(_writer);
            Counters.Rejected++;
            FinishAndSend(PacketHeader.Connectionless(PacketKind.ConnectRejected), 0, to);
        }

        private void SendPathChallenge(NetConnection c, long now)
        {
            var header = new PacketHeader { Kind = PacketKind.PathChallenge, ConnectionId = c.Id, PathData = c.Path.Challenge };
            header.Write(_send);
            c.Path.LastSentMicros = now;
            Counters.PathChallenges++;
            SendRaw(NetProtocol.PathPacketBytes, c.Path.Candidate);
        }

        private void Close(NetConnection c, DisconnectReason reason, bool notifyPeer)
        {
            if (c.State != ConnectionState.Connected) return;
            if (notifyPeer)
            {
                var header = new PacketHeader { Kind = PacketKind.Disconnect, ConnectionId = c.Id };
                header.Write(_send);
                _send[NetProtocol.HeaderBytes] = (byte)reason;
                for (int i = 0; i < _settings.DisconnectRepeats; i++) SendRaw(NetProtocol.DisconnectPacketBytes, c.Address);
            }
            c.State = ConnectionState.Closing;
            c.CloseReason = reason;
            _closing[_closingCount++] = c;
            if (!_processing) FlushClosed();
        }

        private void FlushClosed()
        {
            for (int i = 0; i < _closingCount; i++)
            {
                NetConnection c = _closing[i];
                _closing[i] = null;
                _handler?.OnDisconnected(c.Index, c.CloseReason);
                _table.Release(c);
            }
            _closingCount = 0;
        }

        private void BeginBody() => _writer.Reset(_send, NetProtocol.HeaderBytes, NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes);

        private void FinishAndSend(PacketHeader header, int padTo, in NetAddress to)
        {
            int length = NetProtocol.HeaderBytes + _writer.BytesWritten;
            if (padTo > length)
            {
                _writer.WriteZeroBytes(padTo - length);
                length = padTo;
            }
            header.Write(_send);
            SendRaw(length, to);
        }

        private bool SendRaw(int length, in NetAddress to)
        {
            if (_socket.SendTo(_send, 0, length, to))
            {
                Counters.PacketsSent++;
                Counters.BytesSent += length;
                return true;
            }
            Counters.SendFailures++;
            return false;
        }
    }
}
