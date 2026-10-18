using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;

namespace Arena.Net.Client
{
    /// <summary>
    /// The client end of a game connection (Server-Spec §3.2, §12.3): owns the <see cref="ClientTransport"/>, connects with a
    /// purpose and ticket, and routes what arrives by the <see cref="MessageKind"/> byte — match messages and snapshots to the
    /// attached <see cref="IMatchMessageHandler"/> (<see cref="MatchClientSession"/>), lobby messages (<c>QueueStatus</c>,
    /// <c>MatchAssigned</c>, <c>QueueRejected</c>, <c>ServerNotice</c>) to the <see cref="ILobbyMessageHandler"/>
    /// (<see cref="LobbyClient"/>). The lobby connection becomes the match connection (§6.1): attach the match session before
    /// <c>MatchAssigned</c> arrives (or in its handler) — <c>MatchStart</c> may come in the same packet.
    /// Polled from one thread: <see cref="Update"/> receives everything pending (handlers run inside) and runs the transport
    /// timers. No allocations after construction.
    /// <para>Return to the match (Server-Spec §7, S3.12): once the client holds the slot's ticket (a <c>Match</c>/<c>Rejoin</c>
    /// connect or <see cref="SetRejoinToken"/> from <c>MatchAssigned</c>), a connection lost by the network (timeout) is
    /// re-established by itself: <c>Rejoin</c> handshakes with the ticket after the pauses of
    /// <c>ReconnectSettings.RetrySeconds</c>, until <c>WindowSeconds</c> after the last
    /// packet from the server. A new socket comes from <see cref="SocketFactory"/> when the old one lost its path (Android changed
    /// networks) — also while connected, then the transport only moves the connection to the new address. <c>MatchOver</c> (or an
    /// unknown ticket) ends it with <see cref="ReconnectState.MatchOver"/>. The match session notices the new connection by
    /// <see cref="ClientTransport.Connects"/> and resets its clocks.</para>
    /// </summary>
    public sealed class NetClient : IConnectionHandler
    {
        private const long MicrosPerSecond = 1000000L;

        private readonly BitReader _reader = new BitReader();
        private readonly ReconnectSettings _reconnect;
        private NetToken _rejoinToken;
        private bool _hasRejoinToken;
        private bool _closedOnPurpose;
        private long _lostAtMicros;
        private long _nextAttemptMicros;
        private int _attempt;

        public NetClient(TransportSettings settings, ulong contentHash, INetRandom random, ReconnectSettings reconnect = null)
        {
            Transport = new ClientTransport(settings, contentHash, random, this);
            _reconnect = reconnect ?? ReconnectSettings.CreateDefault();
            _reconnect.Validate();
        }

        public ClientTransport Transport { get; }

        public ReconnectSettings ReconnectSettings => _reconnect;

        /// <summary>
        /// Makes a new socket to the same server when the current one lost its path (null — keep the socket). The replaced socket
        /// is closed here. Called only on a broken path, not per frame.
        /// </summary>
        public Func<IDatagramSocket> SocketFactory { get; set; }

        /// <summary>Lost connections are re-established automatically (the match session turns it off once the match has ended).</summary>
        public bool ReconnectEnabled { get; set; } = true;

        public ReconnectState ReconnectState { get; private set; }

        /// <summary>The ticket of the own slot (for <c>Rejoin</c>) is known.</summary>
        public bool HasRejoinToken => _hasRejoinToken;

        public NetToken RejoinToken => _rejoinToken;

        /// <summary>Clock time of the last packet before the connection was lost (the start of the return window).</summary>
        public long LostAtMicros => _lostAtMicros;

        /// <summary><c>Rejoin</c> handshakes started so far.</summary>
        public int ReconnectAttempts { get; private set; }

        /// <summary>Lost connections re-established so far.</summary>
        public int Reconnects { get; private set; }

        /// <summary>Sockets replaced after a broken path.</summary>
        public int SocketsReplaced { get; private set; }

        /// <summary>Receiver of match messages (set by <see cref="MatchClientSession"/>).</summary>
        public IMatchMessageHandler Match { get; set; }

        /// <summary>Receiver of lobby messages (set by <see cref="LobbyClient"/>).</summary>
        public ILobbyMessageHandler Lobby { get; set; }

        public bool IsConnected => Transport.IsConnected;

        /// <summary>Smoothed RTT of the connection in microseconds, 0 before the first sample.</summary>
        public long RttMicros
        {
            get
            {
                RttEstimator rtt = Transport.Connection.Rtt;
                return rtt.HasSample ? rtt.SmoothedMicros : 0L;
            }
        }

        public long MalformedMessages { get; private set; }
        public long UnknownMessages { get; private set; }

        /// <summary>Connects with a ticket (<c>Match</c>, or <c>Rejoin</c> after a restart of the app); the ticket is kept for returns.</summary>
        public void Connect(IDatagramSocket socket, ConnectPurpose purpose, in NetToken token, long nowMicros)
        {
            SetRejoinToken(in token);
            _closedOnPurpose = false;
            ReconnectState = ReconnectState.None;
            Transport.Connect(socket, purpose, true, token, nowMicros);
        }

        /// <summary>Connects for the lobby: no ticket (§6.4 — the server knows only the connection).</summary>
        public void ConnectLobby(IDatagramSocket socket, long nowMicros)
        {
            _closedOnPurpose = false;
            ReconnectState = ReconnectState.None;
            Transport.Connect(socket, ConnectPurpose.Lobby, false, default, nowMicros);
        }

        /// <summary>The ticket of the own slot (from <c>MatchAssigned</c>): from now on a lost connection returns to the match.</summary>
        public void SetRejoinToken(in NetToken token)
        {
            if (token.IsEmpty) return;
            _rejoinToken = token;
            _hasRejoinToken = true;
        }

        /// <summary>Seconds left of the return window at <paramref name="nowMicros"/> (0 when not reconnecting).</summary>
        public float ReconnectSecondsLeft(long nowMicros)
        {
            if (ReconnectState != ReconnectState.Waiting && ReconnectState != ReconnectState.Connecting) return 0f;
            float left = _reconnect.WindowSeconds - (nowMicros - _lostAtMicros) / (float)MicrosPerSecond;
            return left > 0f ? left : 0f;
        }

        /// <summary>
        /// "Back to the match" after the automatic attempts gave up: a new window from now, the first attempt at once. False — no
        /// ticket, still connected, or the match is over.
        /// </summary>
        public bool RetryRejoin(long nowMicros)
        {
            if (!_hasRejoinToken || Transport.IsConnected || ReconnectState == ReconnectState.MatchOver) return false;
            _closedOnPurpose = false;
            BeginReconnect(nowMicros, nowMicros, 0L);
            return true;
        }

        public void Update(long nowMicros)
        {
            if (SocketFactory != null && Transport.IsConnected && Transport.PathBroken) ReplaceSocket();
            Transport.Update(nowMicros);
            UpdateReconnect(nowMicros);
        }

        /// <summary>Sends an unreliable payload now (with the due reliable messages and acks).</summary>
        public bool Send(byte[] payload, int length, long nowMicros) => Transport.Send(payload, length, nowMicros);

        public bool SendReliable(byte[] message, int offset, int length) => Transport.SendReliable(message, offset, length);

        /// <summary>Closes the connection on purpose: no automatic return.</summary>
        public void Disconnect()
        {
            _closedOnPurpose = true;
            ReconnectState = ReconnectState.None;
            Transport.Disconnect();
        }

        private void UpdateReconnect(long now)
        {
            ClientState state = Transport.State;
            if (state == ClientState.Connected)
            {
                if (ReconnectState == ReconnectState.Connecting)
                {
                    ReconnectState = ReconnectState.None;
                    Reconnects++;
                }
                return;
            }
            if (ReconnectState == ReconnectState.Connecting && state != ClientState.Disconnected)
            {
                // A handshake still running when the window ends is dropped: the window is a promise to the player (§7).
                if (now - _lostAtMicros < SecondsToMicros(_reconnect.WindowSeconds)) return;
                Transport.Disconnect();
                ReconnectState = ReconnectState.GaveUp;
                return;
            }
            if (state != ClientState.Disconnected || !_hasRejoinToken || _closedOnPurpose) return;
            switch (ReconnectState)
            {
                case ReconnectState.None:
                    if (!ReconnectEnabled || !IsLostByNetwork(Transport.DisconnectReason)) return;
                    long lostAt = Transport.Connection.LastReceiveMicros;
                    if (lostAt <= 0L || lostAt > now) lostAt = now;
                    BeginReconnect(now, lostAt, SecondsToMicros(_reconnect.DelayBefore(0)));
                    break;
                case ReconnectState.Connecting:
                    // The attempt ended without a connection.
                    RejectReason reject = Transport.RejectReason;
                    if (Transport.DisconnectReason == DisconnectReason.Rejected && IsFinal(reject))
                    {
                        ReconnectState = reject == RejectReason.MatchOver || reject == RejectReason.InvalidToken
                            ? ReconnectState.MatchOver
                            : ReconnectState.GaveUp;
                        return;
                    }
                    ReconnectState = ReconnectState.Waiting;
                    _nextAttemptMicros = now + SecondsToMicros(_reconnect.DelayBefore(_attempt));
                    break;
            }
            if (ReconnectState != ReconnectState.Waiting) return;
            if (!ReconnectEnabled || now - _lostAtMicros >= SecondsToMicros(_reconnect.WindowSeconds))
            {
                ReconnectState = ReconnectState.GaveUp;
                return;
            }
            if (now < _nextAttemptMicros) return;
            if (SocketFactory != null && (Transport.Socket == null || Transport.Socket.PathBroken)) ReplaceSocket();
            if (Transport.Socket == null) return;
            _attempt++;
            ReconnectAttempts++;
            ReconnectState = ReconnectState.Connecting;
            Transport.Connect(Transport.Socket, ConnectPurpose.Rejoin, true, _rejoinToken, now);
        }

        private void BeginReconnect(long now, long lostAt, long delayMicros)
        {
            _lostAtMicros = lostAt;
            _attempt = 0;
            _nextAttemptMicros = now + delayMicros;
            ReconnectState = ReconnectState.Waiting;
        }

        private void ReplaceSocket()
        {
            IDatagramSocket old = Transport.Socket;
            if (old == null) return;
            IDatagramSocket socket = SocketFactory();
            if (socket == null || socket == old) return;
            Transport.ReplaceSocket(socket);
            old.Close();
            SocketsReplaced++;
        }

        /// <summary>Only a connection lost by the network comes back by itself (not one the server or this client closed).</summary>
        private static bool IsLostByNetwork(DisconnectReason reason) =>
            reason == DisconnectReason.Timeout || reason == DisconnectReason.RateLimited;

        /// <summary>Refusals that trying again will not change.</summary>
        private static bool IsFinal(RejectReason reason) =>
            reason == RejectReason.MatchOver || reason == RejectReason.InvalidToken || reason == RejectReason.VersionMismatch
            || reason == RejectReason.ContentMismatch;

        private static long SecondsToMicros(float seconds) => (long)(seconds * MicrosPerSecond);

        // ---- IConnectionHandler ----

        public void OnReliableMessage(int connection, byte[] buffer, int offset, int length)
        {
            if (length < 1)
            {
                MalformedMessages++;
                return;
            }
            var kind = (MessageKind)buffer[offset];
            switch (kind)
            {
                case MessageKind.MatchStart:
                case MessageKind.MatchEnded:
                case MessageKind.Eliminated:
                    if (Match == null)
                    {
                        UnknownMessages++;
                        return;
                    }
                    _reader.Reset(buffer, offset + 1, length - 1);
                    if (!Match.OnMatchMessage(kind, _reader)) MalformedMessages++;
                    return;
                case MessageKind.MatchAssigned:
                case MessageKind.QueueRejected:
                case MessageKind.ServerNotice:
                    ToLobby(kind, buffer, offset, length);
                    return;
                default:
                    UnknownMessages++;
                    return;
            }
        }

        public void OnPayload(int connection, ushort sequence, byte[] buffer, int offset, int length)
        {
            if (length >= 1 && (MessageKind)buffer[offset] == MessageKind.QueueStatus)
            {
                ToLobby(MessageKind.QueueStatus, buffer, offset, length);
                return;
            }
            if (length < 1 || (MessageKind)buffer[offset] != MessageKind.Snapshot)
            {
                MalformedMessages++;
                return;
            }
            if (Match == null)
            {
                UnknownMessages++;
                return;
            }
            if (!Match.OnSnapshot(buffer, offset + 1, length - 1)) MalformedMessages++;
        }

        public void OnPacketAcked(int connection, ushort sequence) { }

        private void ToLobby(MessageKind kind, byte[] buffer, int offset, int length)
        {
            if (Lobby == null)
            {
                UnknownMessages++;
                return;
            }
            _reader.Reset(buffer, offset + 1, length - 1);
            if (!Lobby.OnLobbyMessage(kind, _reader)) MalformedMessages++;
        }
    }
}
