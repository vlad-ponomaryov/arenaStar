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
    /// (<see cref="ConnectPurpose.Match"/> or <see cref="ConnectPurpose.Rejoin"/>; <see cref="CreateMatch"/> returns the tickets)
    /// or is handed over by the lobby of a <c>ServerNode</c> (<see cref="AssignConnection"/>, S3.8). No allocations in <see cref="Tick"/> (rooms allocate when created).
    /// </summary>
    public sealed class MatchHost : IServerTransportHandler
    {
        private readonly ServerSettings _settings;
        private readonly ContentBundle _bundle;
        private readonly BotDifficultyDef _botDifficulty;
        private readonly INetRandom _random;
        private ServerTransport _transport;
        private readonly MatchRoom[] _rooms;
        private readonly int[] _connRoom;
        private readonly int[] _connSlot;
        private readonly BitReader _reader = new BitReader();
        private readonly InputMessage _input = new InputMessage();

        /// <summary>A standalone match server: owns its <see cref="ServerTransport"/> and is its handler (tests of S3.6–S3.7).</summary>
        public MatchHost(IServerDatagramSocket socket, TransportSettings transportSettings, ServerSettings settings, ContentBundle bundle,
            ulong contentHash, INetRandom random)
            : this(settings, bundle, random, transportSettings?.MaxConnections ?? throw new ArgumentNullException(nameof(transportSettings)))
        {
            _transport = new ServerTransport(socket, transportSettings, contentHash, random, this);
        }

        /// <summary>
        /// The match part of a <c>ServerNode</c> (S3.8): the node owns <paramref name="transport"/>, is its handler and forwards
        /// the events of match connections here; the node calls <see cref="StepRooms"/> instead of <see cref="Tick"/>.
        /// </summary>
        public MatchHost(ServerTransport transport, ServerSettings settings, ContentBundle bundle, INetRandom random)
            : this(settings, bundle, random, transport?.Settings.MaxConnections ?? throw new ArgumentNullException(nameof(transport)))
        {
            _transport = transport;
        }

        private MatchHost(ServerSettings settings, ContentBundle bundle, INetRandom random, int maxConnections)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _botDifficulty = bundle.FindBotDifficulty(settings.FillBotDifficulty)
                ?? throw new ArgumentException("MatchHost: no bot difficulty " + settings.FillBotDifficulty + " in the content pack");
            _rooms = new MatchRoom[settings.MaxMatches];
            _connRoom = new int[maxConnections];
            _connSlot = new int[maxConnections];
            for (int i = 0; i < _connRoom.Length; i++) _connRoom[i] = -1;
        }

        public ServerTransport Transport => _transport;

        public ServerSettings Settings => _settings;

        public ServerMetrics Metrics { get; } = new ServerMetrics();

        public int RoomCapacity => _rooms.Length;

        public ContentBundle Bundle => _bundle;

        /// <summary>Gets every room that enters <see cref="MatchPhase.Ended"/> once (the host process logs the result, S3.9; null — nobody).</summary>
        public IMatchResultSink ResultSink { get; set; }

        /// <summary>Open rooms that have not ended yet (Joining … Running): a stopping server waits for them (§5.3).</summary>
        public int PlayingMatches
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _rooms.Length; i++)
                    if (_rooms[i] != null && _rooms[i].Phase < MatchPhase.Ended) n++;
                return n;
            }
        }

        /// <summary>Earliest clock time a room step is due (<see cref="MatchRoom.NextStepMicros"/>), <see cref="long.MaxValue"/> — no rooms.</summary>
        public long NextStepDueMicros()
        {
            long due = long.MaxValue;
            for (int i = 0; i < _rooms.Length; i++)
            {
                MatchRoom room = _rooms[i];
                if (room == null) continue;
                long next = room.NextStepMicros;
                if (next < due) due = next;
            }
            return due;
        }

        /// <summary>A room can be created now (fewer than <c>MaxMatches</c> are open).</summary>
        public bool HasFreeRoom
        {
            get
            {
                for (int i = 0; i < _rooms.Length; i++)
                    if (_rooms[i] == null) return true;
                return false;
            }
        }

        /// <summary>The connection is bound to a slot of a room.</summary>
        public bool OwnsConnection(int connection) => _connRoom[connection] >= 0;

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

        /// <summary>One pass of the game thread at <paramref name="nowMicros"/> (standalone host).</summary>
        public void Tick(long nowMicros)
        {
            _transport.Receive(nowMicros);
            StepRooms(nowMicros);
            _transport.Update(nowMicros);
        }

        /// <summary>The due steps of every room (at most <c>MaxCatchUpTicks</c> each); closed rooms are removed.</summary>
        public void StepRooms(long nowMicros)
        {
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
                bool wasEnded = room.Phase >= MatchPhase.Ended;
                for (int k = 0; k < due && room.Phase != MatchPhase.Closed; k++) room.Step(nowMicros);
                if (!wasEnded && room.Phase >= MatchPhase.Ended) ResultSink?.OnMatchEnded(room);
                if (room.Phase == MatchPhase.Closed) RemoveRoom(i);
            }
        }

        /// <summary>
        /// Hands an open connection (from the lobby, §6.1) to <paramref name="slot"/> of <paramref name="room"/>: from now on its
        /// messages go to the room, <c>MatchStart</c> follows when the room loads. No reconnect.
        /// </summary>
        public void AssignConnection(int connection, MatchRoom room, int slot)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (_rooms[room.Index] != room) throw new ArgumentException("MatchHost: the room is not open on this host");
            if (_connRoom[connection] >= 0) throw new InvalidOperationException("MatchHost: the connection already has a slot");
            Bind(connection, room.Index, slot);
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
            if (purpose == ConnectPurpose.Lobby || !hasToken) reason = RejectReason.InvalidToken; // lobby: ServerNode
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
            Bind(connection, room, slot);
        }

        private void Bind(int connection, int room, int slot)
        {
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
