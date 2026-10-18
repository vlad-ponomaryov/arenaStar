using System;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Match;
using Arena.Net.Transport;

namespace Arena.Net.Server.Lobby
{
    /// <summary>
    /// One server process = one node (Server-Spec §5.3, §6.1): lobby + matches on one UDP socket. Owns the
    /// <see cref="ServerTransport"/> and routes its events — connections that came for the lobby (<c>purpose = Lobby</c>) to the
    /// <see cref="Matchmaker"/> until it hands them to a match, match and rejoin connections (tickets) and handed-over ones to the
    /// <see cref="MatchHost"/>. When the match room closes, a connection that came for the lobby is a lobby connection again
    /// ("Again"). <see cref="Update"/> is the single game thread pass: receive → lobby (assembly, statuses) → due match steps →
    /// transport timers and flushes. <see cref="Maintenance"/>: new lobby connections and joins are refused
    /// (<c>Maintenance</c>), queued clients lose their tickets, everybody gets <c>ServerNotice</c>, running matches play on; the
    /// LAN discovery answer shows the flag. No allocations in <see cref="Update"/> except when a match is assembled.
    /// </summary>
    public sealed class ServerNode : IServerTransportHandler
    {
        private readonly bool[] _lobby;
        private readonly byte[] _packet = new byte[NetLimits.MaxReliableMessageBytes];
        private readonly BitWriter _writer = new BitWriter();
        private long _now;

        public ServerNode(IServerDatagramSocket socket, TransportSettings transportSettings, ServerSettings settings, ContentBundle bundle,
            ulong contentHash, INetRandom random)
        {
            if (transportSettings == null) throw new ArgumentNullException(nameof(transportSettings));
            if (random == null) throw new ArgumentNullException(nameof(random));
            Transport = new ServerTransport(socket, transportSettings, contentHash, random, this);
            Matches = new MatchHost(Transport, settings, bundle, random);
            Matchmaker = new Matchmaker(Transport, Matches, settings, random, LobbyMetrics);
            _lobby = new bool[transportSettings.MaxConnections];
        }

        public ServerTransport Transport { get; }

        public MatchHost Matches { get; }

        public Matchmaker Matchmaker { get; }

        public LobbyMetrics LobbyMetrics { get; } = new LobbyMetrics();

        public bool Maintenance { get; private set; }

        /// <summary>Turns maintenance on or off (the host process watches <c>maintenance.flag</c> or its config, S3.9).</summary>
        public void SetMaintenance(bool on)
        {
            if (on == Maintenance) return;
            Maintenance = on;
            Transport.Maintenance = on;
            Matchmaker.SetMaintenance(on);
            if (!on) return;
            var notice = new ServerNoticeMessage { Kind = ServerNoticeKind.Maintenance, Seconds = 0 };
            _writer.Reset(_packet, 0, _packet.Length);
            notice.Write(_writer);
            ConnectionTable table = Transport.Connections;
            for (int i = 0; i < table.ActiveCount; i++)
            {
                int connection = table.ActiveAt(i);
                if (!Transport.SendReliable(connection, _packet, 0, _writer.BytesWritten)) LobbyMetrics.ReliableSendFailures++;
            }
        }

        /// <summary>The connection came for the lobby (it may be in a match now).</summary>
        public bool IsLobbyConnection(int connection) => _lobby[connection];

        /// <summary>One pass of the game thread at <paramref name="nowMicros"/>.</summary>
        public void Update(long nowMicros)
        {
            _now = nowMicros;
            Transport.Receive(nowMicros);
            Matchmaker.Update(nowMicros);
            Matches.StepRooms(nowMicros);
            Transport.Update(nowMicros);
        }

        // ---- IServerTransportHandler ----

        public RejectReason Approve(ConnectPurpose purpose, bool hasToken, in NetToken token)
        {
            if (purpose != ConnectPurpose.Lobby) return Matches.Approve(purpose, hasToken, in token);
            return Maintenance ? RejectReason.Maintenance : RejectReason.None;
        }

        public void OnConnected(int connection)
        {
            NetConnection c = Transport.GetConnection(connection);
            if (c != null && c.Purpose == ConnectPurpose.Lobby)
            {
                _lobby[connection] = true;
                LobbyMetrics.LobbyConnections++;
                return;
            }
            Matches.OnConnected(connection);
        }

        public void OnDisconnected(int connection, DisconnectReason reason)
        {
            if (_lobby[connection])
            {
                _lobby[connection] = false;
                LobbyMetrics.LobbyConnections--;
                Matchmaker.OnDisconnected(connection);
            }
            Matches.OnDisconnected(connection, reason);
        }

        public void OnReliableMessage(int connection, byte[] buffer, int offset, int length)
        {
            if (Matches.OwnsConnection(connection)) Matches.OnReliableMessage(connection, buffer, offset, length);
            else if (_lobby[connection]) Matchmaker.OnReliableMessage(connection, buffer, offset, length, _now);
        }

        public void OnPayload(int connection, ushort sequence, byte[] buffer, int offset, int length)
        {
            // A lobby client sends empty keepalive packets only; inputs belong to matches.
            if (Matches.OwnsConnection(connection)) Matches.OnPayload(connection, sequence, buffer, offset, length);
        }

        public void OnPacketAcked(int connection, ushort sequence)
        {
            if (Matches.OwnsConnection(connection)) Matches.OnPacketAcked(connection, sequence);
        }
    }
}
