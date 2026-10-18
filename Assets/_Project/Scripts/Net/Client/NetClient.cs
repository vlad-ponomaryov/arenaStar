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
    /// </summary>
    public sealed class NetClient : IConnectionHandler
    {
        private readonly BitReader _reader = new BitReader();

        public NetClient(TransportSettings settings, ulong contentHash, INetRandom random)
        {
            Transport = new ClientTransport(settings, contentHash, random, this);
        }

        public ClientTransport Transport { get; }

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

        public void Connect(IDatagramSocket socket, ConnectPurpose purpose, in NetToken token, long nowMicros) =>
            Transport.Connect(socket, purpose, true, token, nowMicros);

        /// <summary>Connects for the lobby: no ticket (§6.4 — the server knows only the connection).</summary>
        public void ConnectLobby(IDatagramSocket socket, long nowMicros) =>
            Transport.Connect(socket, ConnectPurpose.Lobby, false, default, nowMicros);

        public void Update(long nowMicros) => Transport.Update(nowMicros);

        /// <summary>Sends an unreliable payload now (with the due reliable messages and acks).</summary>
        public bool Send(byte[] payload, int length, long nowMicros) => Transport.Send(payload, length, nowMicros);

        public bool SendReliable(byte[] message, int offset, int length) => Transport.SendReliable(message, offset, length);

        public void Disconnect() => Transport.Disconnect();

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
