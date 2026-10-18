using System;
using Arena.Bots.Defs;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// The single-threaded match server (Server-Spec §5.3): owns the <see cref="ServerTransport"/>, the rooms and the map
    /// "connection → room, slot". <see cref="Tick"/> — the same in tests (virtual clock, <c>InMemoryNetwork</c>) and in the
    /// .NET host (S3.9): receive every datagram (inputs into the slots' buffers, <c>ClientReady</c>, <c>Leave</c>,
    /// acknowledgements of snapshot packets) → the due steps of every room (at most <c>MaxCatchUpTicks</c>, the rest is
    /// skipped and counted) → transport timers and flushes. A player connects with the ticket of his slot
    /// (<see cref="ConnectPurpose.Match"/> or <see cref="ConnectPurpose.Rejoin"/>); the lobby (S3.8) will hand out tickets, here
    /// <see cref="CreateMatch"/> returns them. No allocations in <see cref="Tick"/> (rooms allocate when created).
    /// </summary>
    public sealed class MatchHost : IServerTransportHandler
    {
        private readonly ServerSettings _settings;
        private readonly ContentBundle _bundle;
        private readonly BotDifficultyDef _botDifficulty;
        private readonly INetRandom _random;
        private readonly ServerTransport _transport;
        private readonly MatchRoom[] _rooms;
        private readonly int[] _connRoom;
        private readonly int[] _connSlot;
        private readonly BitReader _reader = new BitReader();
        private readonly InputMessage _input = new InputMessage();

        public MatchHost(IServerDatagramSocket socket, TransportSettings transportSettings, ServerSettings settings, ContentBundle bundle,
            ulong contentHash, INetRandom random)
        {
            if (transportSettings == null) throw new ArgumentNullException(nameof(transportSettings));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _botDifficulty = bundle.FindBotDifficulty(settings.FillBotDifficulty)
                ?? throw new ArgumentException("MatchHost: no bot difficulty " + settings.FillBotDifficulty + " in the content pack");
            _transport = new ServerTransport(socket, transportSettings, contentHash, random, this);
            _rooms = new MatchRoom[settings.MaxMatches];
            _connRoom = new int[transportSettings.MaxConnections];
            _connSlot = new int[transportSettings.MaxConnections];
            for (int i = 0; i < _connRoom.Length; i++) _connRoom[i] = -1;
        }

        public ServerTransport Transport => _transport;

        public ServerSettings Settings => _settings;

        public ServerMetrics Metrics { get; } = new ServerMetrics();

        public int RoomCapacity => _rooms.Length;

        /// <summary>Room by index, or null (closed rooms are removed).</summary>
        public MatchRoom GetRoom(int index) => _rooms[index];

        /// <summary>
        /// Opens a room for <paramref name="request"/>; every player slot gets a random 128-bit ticket
        /// (<see cref="TokenOf"/>). Null if <c>MaxMatches</c> rooms are open.
        /// </summary>
        public MatchRoom CreateMatch(MatchRoomRequest request, long nowMicros)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            int index = -1;
            for (int i = 0; i < _rooms.Length && index < 0; i++)
                if (_rooms[i] == null) index = i;
            if (index < 0) return null;
            int slots = request.Setup.SlotCount;
            var tokens = new NetToken[slots];
            for (int i = 0; i < slots; i++)
            {
                NetToken t;
                do t = new NetToken(_random.NextULong(), _random.NextULong());
                while (t.IsEmpty);
                tokens[i] = t;
            }
            var room = new MatchRoom(index, _random.NextULong(), request, tokens, _settings, _transport, Metrics, _bundle.BotBehaviour,
                _botDifficulty, nowMicros);
            _rooms[index] = room;
            Metrics.MatchesCreated++;
            Metrics.ActiveMatches++;
            return room;
        }

        public static NetToken TokenOf(MatchRoom room, int slot) => room.GetSlot(slot).Token;

        /// <summary>One pass of the game thread at <paramref name="nowMicros"/>.</summary>
        public void Tick(long nowMicros)
        {
            _transport.Receive(nowMicros);
            for (int i = 0; i < _rooms.Length; i++)
            {
                MatchRoom room = _rooms[i];
                if (room == null) continue;
                int due = room.TicksDue(nowMicros);
                if (due > _settings.MaxCatchUpTicks)
                {
                    room.SkipSteps(due - _settings.MaxCatchUpTicks);
                    Metrics.SkippedTicks += due - _settings.MaxCatchUpTicks;
                    due = _settings.MaxCatchUpTicks;
                }
                for (int k = 0; k < due && room.Phase != MatchPhase.Closed; k++) room.Step(nowMicros);
                if (room.Phase == MatchPhase.Closed) RemoveRoom(i);
            }
            _transport.Update(nowMicros);
        }

        private void RemoveRoom(int index)
        {
            for (int c = 0; c < _connRoom.Length; c++)
            {
                if (_connRoom[c] != index) continue;
                _connRoom[c] = -1;
                Metrics.ConnectedPlayers--;
            }
            _rooms[index] = null;
            Metrics.ActiveMatches--;
        }

        private bool TryFind(in NetToken token, out int room, out int slot)
        {
            for (int i = 0; i < _rooms.Length; i++)
            {
                if (_rooms[i] == null) continue;
                int s = _rooms[i].FindSlot(in token);
                if (s < 0) continue;
                room = i;
                slot = s;
                return true;
            }
            room = slot = -1;
            return false;
        }

        // ---- IServerTransportHandler ----

        public RejectReason Approve(ConnectPurpose purpose, bool hasToken, in NetToken token)
        {
            RejectReason reason = RejectReason.None;
            if (purpose == ConnectPurpose.Lobby || !hasToken) reason = RejectReason.InvalidToken; // the lobby comes in S3.8
            else if (!TryFind(in token, out int room, out _)) reason = RejectReason.InvalidToken;
            else if (_rooms[room].Phase == MatchPhase.Closed) reason = RejectReason.MatchOver;
            if (reason != RejectReason.None) Metrics.RejectedConnections++;
            return reason;
        }

        public void OnConnected(int connection)
        {
            NetConnection c = _transport.GetConnection(connection);
            if (c == null || !c.HasToken || !TryFind(c.Token, out int room, out int slot))
            {
                _transport.Disconnect(connection, DisconnectReason.Rejected);
                return;
            }
            MatchRoom r = _rooms[room];
            int old = r.GetSlot(slot).Connection;
            if (old >= 0 && old != connection)
            {
                // The same ticket again (a new address or a restarted client): the newest connection wins (§7).
                _connRoom[old] = -1;
                r.Unbind(slot);
                _transport.Disconnect(old, DisconnectReason.Closed);
            }
            _connRoom[connection] = room;
            _connSlot[connection] = slot;
            r.Bind(slot, connection);
        }

        public void OnDisconnected(int connection, DisconnectReason reason)
        {
            int room = _connRoom[connection];
            if (room < 0) return;
            _connRoom[connection] = -1;
            MatchRoom r = _rooms[room];
            int slot = _connSlot[connection];
            if (r != null && r.GetSlot(slot).Connection == connection) r.Unbind(slot);
        }

        public void OnReliableMessage(int connection, byte[] buffer, int offset, int length)
        {
            int room = _connRoom[connection];
            if (room < 0) return;
            if (length < 1)
            {
                Metrics.MalformedMessages++;
                return;
            }
            switch ((MessageKind)buffer[offset])
            {
                case MessageKind.ClientReady:
                    _rooms[room].SetReady(_connSlot[connection]);
                    break;
                case MessageKind.Leave:
                    _rooms[room].Leave(_connSlot[connection]);
                    break;
                default:
                    Metrics.MalformedMessages++;
                    break;
            }
        }

        public void OnPayload(int connection, ushort sequence, byte[] buffer, int offset, int length)
        {
            int room = _connRoom[connection];
            if (room < 0) return;
            if (length < 1 || (MessageKind)buffer[offset] != MessageKind.Input)
            {
                Metrics.MalformedMessages++;
                return;
            }
            _reader.Reset(buffer, offset + 1, length - 1);
            if (!_input.Read(_reader))
            {
                Metrics.MalformedMessages++;
                return;
            }
            _rooms[room].OnInput(_connSlot[connection], _input);
        }

        public void OnPacketAcked(int connection, ushort sequence)
        {
            int room = _connRoom[connection];
            if (room < 0) return;
            _rooms[room].OnPacketAcked(_connSlot[connection], sequence);
        }
    }
}
