using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Transport
{
    /// <summary>
    /// The client end of the transport (Server-Spec §3.2): handshake with retries and timeout, one <see cref="NetConnection"/>,
    /// keepalive and timeout, answers to path challenges, socket replacement on address change with the same connection id.
    /// Polled from the main thread: <see cref="Update"/> receives everything pending and runs the timers. No allocations
    /// after construction.
    /// </summary>
    public sealed class ClientTransport
    {
        private const long MicrosPerMs = 1000L;
        private const int ConnectionIndex = 0;

        private readonly TransportSettings _settings;
        private readonly ulong _contentHash;
        private readonly INetRandom _random;
        private readonly IConnectionHandler _handler;
        private readonly NetConnection _connection = new NetConnection(ConnectionIndex);
        private readonly byte[] _recv = new byte[NetLimits.ReceiveBufferBytes];
        private readonly byte[] _send = new byte[NetLimits.MaxPacketBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();

        private IDatagramSocket _socket;
        private ConnectPurpose _purpose;
        private bool _hasToken;
        private NetToken _token;
        private ulong _nonce;
        private ulong _cookie;
        private long _connectStartedAt;
        private long _lastHandshakeSentAt;

        public ClientTransport(TransportSettings settings, ulong contentHash, INetRandom random, IConnectionHandler handler)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _contentHash = contentHash;
            _handler = handler;
        }

        public ClientState State { get; private set; }

        public DisconnectReason DisconnectReason { get; private set; }

        public RejectReason RejectReason { get; private set; }

        public NetConnection Connection => _connection;

        public bool IsConnected => State == ClientState.Connected;

        /// <summary>The socket reported a broken local path: create a new one and call <see cref="ReplaceSocket"/>.</summary>
        public bool PathBroken => _socket != null && _socket.PathBroken;

        public long HandshakePacketsSent { get; private set; }
        public long PathResponsesSent { get; private set; }
        public long DroppedPackets { get; private set; }

        /// <summary>Starts the handshake over <paramref name="socket"/> (connected to the server). The socket is not owned.</summary>
        public void Connect(IDatagramSocket socket, ConnectPurpose purpose, bool hasToken, NetToken token, long nowMicros)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _purpose = purpose;
            _hasToken = hasToken;
            _token = token;
            _nonce = _random.NextULong();
            _cookie = 0ul;
            _connectStartedAt = nowMicros;
            DisconnectReason = DisconnectReason.None;
            RejectReason = RejectReason.None;
            _connection.State = ConnectionState.Free;
            State = ClientState.Requesting;
            SendRequest(nowMicros);
        }

        /// <summary>
        /// Continues the same connection over a new socket (the old one lost its interface, §3.2): the next packets come from a
        /// new address and the server moves the connection after a path challenge.
        /// </summary>
        public void ReplaceSocket(IDatagramSocket socket)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        public void Update(long nowMicros)
        {
            if (_socket == null) return;
            while (State != ClientState.Idle && _socket.TryReceive(_recv, out int length)) HandleDatagram(length, nowMicros);
            switch (State)
            {
                case ClientState.Requesting:
                case ClientState.Responding:
                    if (nowMicros - _connectStartedAt >= _settings.ConnectTimeoutMs * MicrosPerMs)
                    {
                        End(DisconnectReason.ConnectTimeout);
                        break;
                    }
                    if (nowMicros - _lastHandshakeSentAt >= _settings.HandshakeRetryMs * MicrosPerMs)
                    {
                        if (State == ClientState.Requesting) SendRequest(nowMicros);
                        else SendResponse(nowMicros);
                    }
                    break;
                case ClientState.Connected:
                    if (_connection.IsTimedOut(nowMicros))
                    {
                        End(DisconnectReason.Timeout);
                        break;
                    }
                    if (_connection.NeedsFlush(nowMicros, _settings.AckDelayMs * MicrosPerMs)) SendData(null, 0, nowMicros);
                    break;
            }
        }

        /// <summary>Sends a data packet with the payload (may be empty) and the due reliable messages.</summary>
        public bool Send(byte[] payload, int payloadLength, long nowMicros) =>
            State == ClientState.Connected && SendData(payload, payloadLength, nowMicros);

        /// <summary>Queues a reliable message (only when connected). False — not connected or the buffer is full.</summary>
        public bool SendReliable(byte[] message, int offset, int length) =>
            State == ClientState.Connected && _connection.Reliable.Enqueue(message, offset, length);

        /// <summary>Closes the connection; <c>Disconnect</c> goes out <c>DisconnectRepeats</c> times.</summary>
        public void Disconnect()
        {
            if (State == ClientState.Connected)
            {
                var header = new PacketHeader { Kind = PacketKind.Disconnect, ConnectionId = _connection.Id };
                header.Write(_send);
                _send[NetProtocol.HeaderBytes] = (byte)DisconnectReason.Closed;
                for (int i = 0; i < _settings.DisconnectRepeats; i++) _socket.Send(_send, 0, NetProtocol.DisconnectPacketBytes);
            }
            if (State != ClientState.Idle && State != ClientState.Disconnected) End(DisconnectReason.Closed);
        }

        private void HandleDatagram(int length, long now)
        {
            if (length > NetLimits.MaxPacketBytes || !PacketHeader.TryRead(_recv, length, out PacketHeader header))
            {
                DroppedPackets++;
                return;
            }
            switch (header.Kind)
            {
                case PacketKind.Data:
                    if (State == ClientState.Connected && header.ConnectionId == _connection.Id
                        && _connection.ProcessPacket(header, _recv, length, now, _handler)) return;
                    break;
                case PacketKind.ConnectChallenge:
                    if (State == ClientState.Requesting && OnChallenge(length, now)) return;
                    break;
                case PacketKind.ConnectAccepted:
                    if ((State == ClientState.Requesting || State == ClientState.Responding) && OnAccepted(header, length, now)) return;
                    break;
                case PacketKind.ConnectRejected:
                    if ((State == ClientState.Requesting || State == ClientState.Responding) && OnRejected(length)) return;
                    break;
                case PacketKind.Disconnect:
                    if (State == ClientState.Connected && header.ConnectionId == _connection.Id && length >= NetProtocol.DisconnectPacketBytes)
                    {
                        var reason = (DisconnectReason)_recv[NetProtocol.HeaderBytes];
                        End(reason == DisconnectReason.None ? DisconnectReason.Closed : reason);
                        return;
                    }
                    break;
                case PacketKind.PathChallenge:
                    if (State == ClientState.Connected && header.ConnectionId == _connection.Id)
                    {
                        var response = new PacketHeader { Kind = PacketKind.PathResponse, ConnectionId = _connection.Id, PathData = header.PathData };
                        response.Write(_send);
                        _socket.Send(_send, 0, NetProtocol.PathPacketBytes);
                        PathResponsesSent++;
                        return;
                    }
                    break;
            }
            DroppedPackets++;
        }

        private bool OnChallenge(int length, long now)
        {
            _reader.Reset(_recv, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!ConnectChallengeMessage.Read(_reader, out ConnectChallengeMessage challenge) || challenge.Nonce != _nonce) return false;
            _cookie = challenge.Cookie;
            State = ClientState.Responding;
            SendResponse(now);
            return true;
        }

        private bool OnAccepted(in PacketHeader header, int length, long now)
        {
            _reader.Reset(_recv, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!ConnectAcceptedMessage.Read(_reader, out ConnectAcceptedMessage accepted) || accepted.Nonce != _nonce
                || header.ConnectionId == 0ul) return false;
            _connection.Open(header.ConnectionId, default, _settings, now);
            if (accepted.KeepaliveMs > 0u) _connection.KeepaliveMicros = accepted.KeepaliveMs * MicrosPerMs;
            if (accepted.DisconnectTimeoutMs > 0u) _connection.TimeoutMicros = accepted.DisconnectTimeoutMs * MicrosPerMs;
            _connection.Nonce = _nonce;
            State = ClientState.Connected;
            return true;
        }

        private bool OnRejected(int length)
        {
            _reader.Reset(_recv, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!ConnectRejectedMessage.Read(_reader, out ConnectRejectedMessage rejected) || rejected.Nonce != _nonce) return false;
            RejectReason = rejected.Reason;
            End(DisconnectReason.Rejected);
            return true;
        }

        private void End(DisconnectReason reason)
        {
            DisconnectReason = reason;
            State = ClientState.Disconnected;
            if (_connection.State == ConnectionState.Connected) _connection.State = ConnectionState.Free;
        }

        private void SendRequest(long now)
        {
            var request = new ConnectRequestMessage
            {
                ProtocolId = NetProtocol.Id,
                ProtocolVersion = NetProtocol.Version,
                ContentHash = _contentHash,
                Purpose = _purpose,
                Nonce = _nonce,
            };
            BeginBody();
            request.Write(_writer);
            FinishHandshake(PacketKind.ConnectRequest, now);
        }

        private void SendResponse(long now)
        {
            var response = new ConnectResponseMessage
            {
                ProtocolId = NetProtocol.Id,
                ProtocolVersion = NetProtocol.Version,
                ContentHash = _contentHash,
                Purpose = _purpose,
                Nonce = _nonce,
                Cookie = _cookie,
                HasToken = _hasToken,
                Token = _token,
            };
            BeginBody();
            response.Write(_writer);
            FinishHandshake(PacketKind.ConnectResponse, now);
        }

        private void BeginBody() => _writer.Reset(_send, NetProtocol.HeaderBytes, NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes);

        private void FinishHandshake(PacketKind kind, long now)
        {
            int length = NetProtocol.HeaderBytes + _writer.BytesWritten;
            _writer.WriteZeroBytes(NetProtocol.HandshakePacketBytes - length);
            PacketHeader.Connectionless(kind).Write(_send);
            _socket.Send(_send, 0, NetProtocol.HandshakePacketBytes);
            _lastHandshakeSentAt = now;
            HandshakePacketsSent++;
        }

        private bool SendData(byte[] payload, int payloadLength, long now)
        {
            int length = _connection.BuildPacket(_send, now, payload, payloadLength);
            return _socket.Send(_send, 0, length);
        }
    }
}
