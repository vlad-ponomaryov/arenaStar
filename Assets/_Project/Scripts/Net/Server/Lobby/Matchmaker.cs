using System;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Match;
using Arena.Net.Transport;

namespace Arena.Net.Server.Lobby
{
    /// <summary>
    /// The lobby of a node (Server-Spec §6.1–6.2): a FIFO <see cref="ModeQueue"/> per online mode, one <see cref="QueueTicket"/>
    /// per connection. <c>QueueJoin</c> is checked (mode playable here, fighter index in the content pack, no maintenance) —
    /// otherwise <c>QueueRejected</c>; a second join of the same connection replaces its ticket (back of the queue).
    /// <see cref="Update"/>: a full match (<c>TeamSize × Teams</c> tickets) is assembled at once; when the oldest ticket has waited
    /// <c>BotFillSeconds</c> the match is assembled from who is there and bots take the rest. The room is opened on the
    /// <see cref="MatchHost"/>, every player gets <c>MatchAssigned</c> and his lobby connection becomes the slot's connection —
    /// <c>MatchStart</c> follows on it. Queued clients get <c>QueueStatus</c> every <c>QueueStatusIntervalSeconds</c>.
    /// A closed connection loses its ticket (the transport closes it after <c>DisconnectTimeoutSeconds</c> without packets).
    /// No allocations except when a match is assembled.
    /// </summary>
    public sealed class Matchmaker
    {
        private const long MicrosPerSecond = 1000000L;

        private readonly ServerTransport _transport;
        private readonly MatchHost _host;
        private readonly ServerSettings _settings;
        private readonly INetRandom _random;
        private readonly RoomAssembler _assembler;
        private readonly QueueTicket[] _tickets;
        private readonly ModeQueue[] _queues;
        private readonly int[] _groupConnections = new int[SimLimits.MaxFighters];
        private readonly int[] _groupFighters = new int[SimLimits.MaxFighters];
        private readonly int[] _groupSlots = new int[SimLimits.MaxFighters];
        private readonly byte[] _packet = new byte[NetLimits.MaxReliableMessageBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();
        private readonly long _botFillMicros;
        private readonly long _statusMicros;

        public Matchmaker(ServerTransport transport, MatchHost host, ServerSettings settings, INetRandom random, LobbyMetrics metrics)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _assembler = new RoomAssembler(host.Bundle);
            int maxConnections = transport.Settings.MaxConnections;
            _tickets = new QueueTicket[maxConnections];
            for (int i = 0; i < maxConnections; i++) _tickets[i] = new QueueTicket();

            var modes = host.Bundle.ModeMaps;
            int playable = 0;
            for (int i = 0; i < modes.Length; i++)
                if (_assembler.IsPlayable(modes[i].Mode)) playable++;
            _queues = new ModeQueue[playable];
            int q = 0;
            for (int i = 0; i < modes.Length; i++)
            {
                ModeKind mode = modes[i].Mode;
                if (!_assembler.IsPlayable(mode)) continue;
                int capacity = _assembler.Capacity(mode, out int teamSize);
                _queues[q++] = new ModeQueue(mode, capacity, teamSize, maxConnections);
            }
            _botFillMicros = (long)(settings.BotFillSeconds * MicrosPerSecond);
            _statusMicros = (long)(settings.QueueStatusIntervalSeconds * MicrosPerSecond);
        }

        public LobbyMetrics Metrics { get; }

        public RoomAssembler Assembler => _assembler;

        /// <summary>No new tickets (<c>QueueJoin</c> → <see cref="QueueRejectReason.Maintenance"/>); set by the node.</summary>
        public bool Maintenance { get; private set; }

        public int QueueCount => _queues.Length;

        public ModeQueue GetQueue(int index) => _queues[index];

        /// <summary>The queue of <paramref name="mode"/>, or null if the mode is not played here.</summary>
        public ModeQueue QueueOf(ModeKind mode)
        {
            for (int i = 0; i < _queues.Length; i++)
                if (_queues[i].Mode == mode) return _queues[i];
            return null;
        }

        /// <summary>The ticket of <paramref name="connection"/> (check <see cref="QueueTicket.Active"/>).</summary>
        public QueueTicket TicketOf(int connection) => _tickets[connection];

        /// <summary>Turns maintenance on or off; on — every queued client gets <c>QueueRejected(Maintenance)</c> and loses his ticket.</summary>
        public void SetMaintenance(bool on)
        {
            Maintenance = on;
            if (!on) return;
            for (int q = 0; q < _queues.Length; q++)
            {
                ModeQueue queue = _queues[q];
                while (queue.Count > 0)
                {
                    int connection = queue.At(0);
                    RemoveTicket(connection);
                    SendRejected(connection, QueueRejectReason.Maintenance);
                    Metrics.MaintenanceDrops++;
                }
            }
        }

        /// <summary>A lobby message (reliable) of a connection that is not in a match.</summary>
        public void OnReliableMessage(int connection, byte[] buffer, int offset, int length, long nowMicros)
        {
            if (length < 1)
            {
                Metrics.MalformedMessages++;
                return;
            }
            switch ((MessageKind)buffer[offset])
            {
                case MessageKind.QueueJoin:
                    _reader.Reset(buffer, offset + 1, length - 1);
                    var join = new QueueJoinMessage();
                    if (!join.Read(_reader))
                    {
                        Metrics.MalformedMessages++;
                        return;
                    }
                    Join(connection, join.Mode, join.FighterIndex, nowMicros);
                    break;
                case MessageKind.QueueCancel:
                    if (RemoveTicket(connection)) Metrics.Cancels++;
                    break;
                default:
                    Metrics.MalformedMessages++;
                    break;
            }
        }

        /// <summary>The connection closed (or moved to a match): its ticket, if any, is gone.</summary>
        public void OnDisconnected(int connection)
        {
            if (RemoveTicket(connection)) Metrics.DroppedTickets++;
        }

        /// <summary>Joins (or rejoins) the queue; the reason is also sent to the client when not <c>None</c>.</summary>
        public QueueRejectReason Join(int connection, ModeKind mode, int fighterIndex, long nowMicros)
        {
            QueueRejectReason reason = QueueRejectReason.None;
            ModeQueue queue = QueueOf(mode);
            if (Maintenance) reason = QueueRejectReason.Maintenance;
            else if (_host.OwnsConnection(connection)) reason = QueueRejectReason.InMatch;
            else if (queue == null) reason = QueueRejectReason.InvalidMode;
            else if (fighterIndex < 0 || fighterIndex >= _host.Bundle.Fighters.Length) reason = QueueRejectReason.InvalidFighter;
            RemoveTicket(connection);
            if (reason != QueueRejectReason.None)
            {
                Metrics.RejectedJoins++;
                SendRejected(connection, reason);
                return reason;
            }
            QueueTicket t = _tickets[connection];
            t.Active = true;
            t.Connection = connection;
            t.Mode = mode;
            t.FighterIndex = fighterIndex;
            t.EnqueuedMicros = nowMicros;
            t.LastStatusMicros = nowMicros - _statusMicros; // the first status goes out with this update
            queue.Add(connection);
            Metrics.Joins++;
            Metrics.QueuedPlayers++;
            return QueueRejectReason.None;
        }

        /// <summary>Assembles the due matches, then sends the due statuses.</summary>
        public void Update(long nowMicros)
        {
            for (int q = 0; q < _queues.Length; q++)
            {
                ModeQueue queue = _queues[q];
                while (queue.Count > 0)
                {
                    bool full = queue.Count >= queue.Capacity;
                    bool fill = !full && nowMicros - _tickets[queue.At(0)].EnqueuedMicros >= _botFillMicros;
                    if (!full && !fill) break;
                    if (!_host.HasFreeRoom)
                    {
                        Metrics.NoFreeRoom++;
                        break;
                    }
                    Assemble(queue, full ? queue.Capacity : queue.Count, nowMicros);
                }
            }
            for (int q = 0; q < _queues.Length; q++)
            {
                ModeQueue queue = _queues[q];
                for (int i = 0; i < queue.Count; i++)
                {
                    QueueTicket t = _tickets[queue.At(i)];
                    if (nowMicros - t.LastStatusMicros < _statusMicros) continue;
                    t.LastStatusMicros = nowMicros;
                    SendStatus(t, queue, nowMicros);
                }
            }
        }

        private void Assemble(ModeQueue queue, int count, long nowMicros)
        {
            for (int k = 0; k < count; k++)
            {
                int connection = queue.At(k);
                _groupConnections[k] = connection;
                _groupFighters[k] = _tickets[connection].FighterIndex;
                long wait = nowMicros - _tickets[connection].EnqueuedMicros;
                if (wait > Metrics.MaxWaitMicros) Metrics.MaxWaitMicros = wait;
            }
            int seed = (int)(_random.NextULong() & int.MaxValue);
            MatchRoomRequest request = _assembler.Assemble(queue.Mode, _groupFighters, count, seed, _groupSlots);
            MatchRoom room = _host.CreateMatch(request, nowMicros);
            queue.RemoveFront(count);
            Metrics.QueuedPlayers -= count;
            Metrics.MatchesAssembled++;
            if (count < queue.Capacity)
            {
                Metrics.BotFilledMatches++;
                Metrics.FillBots += queue.Capacity - count;
            }
            for (int k = 0; k < count; k++)
            {
                int connection = _groupConnections[k];
                int slot = _groupSlots[k];
                _tickets[connection].Clear();
                var assigned = new MatchAssignedMessage
                {
                    MatchId = room.MatchId,
                    RejoinToken = room.GetSlot(slot).Token,
                    Slot = slot,
                };
                _writer.Reset(_packet, 0, _packet.Length);
                assigned.Write(_writer);
                SendReliable(connection);
                _host.AssignConnection(connection, room, slot);
            }
        }

        private bool RemoveTicket(int connection)
        {
            QueueTicket t = _tickets[connection];
            if (!t.Active) return false;
            QueueOf(t.Mode)?.Remove(connection);
            t.Clear();
            Metrics.QueuedPlayers--;
            return true;
        }

        private void SendStatus(QueueTicket t, ModeQueue queue, long nowMicros)
        {
            var status = new QueueStatusMessage
            {
                State = QueueState.Searching,
                Mode = queue.Mode,
                WaitSeconds = (int)((nowMicros - t.EnqueuedMicros) / MicrosPerSecond),
                Found = Math.Min(queue.Count, queue.Capacity),
                Needed = queue.Capacity,
            };
            _writer.Reset(_packet, 0, _packet.Length);
            status.Write(_writer);
            if (_transport.Send(t.Connection, _packet, _writer.BytesWritten, nowMicros)) Metrics.StatusesSent++;
        }

        private void SendRejected(int connection, QueueRejectReason reason)
        {
            var rejected = new QueueRejectedMessage { Reason = reason };
            _writer.Reset(_packet, 0, _packet.Length);
            rejected.Write(_writer);
            SendReliable(connection);
        }

        private void SendReliable(int connection)
        {
            if (!_transport.SendReliable(connection, _packet, 0, _writer.BytesWritten)) Metrics.ReliableSendFailures++;
        }
    }
}
