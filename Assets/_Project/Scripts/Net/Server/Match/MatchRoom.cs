using System;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Snapshots;
using Arena.Net.Snapshots;
using Arena.Net.Transport;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// One match on the server (Server-Spec §5.3): <see cref="MatchPhase.Joining"/> → <see cref="MatchPhase.Loading"/>
    /// (<c>MatchStart</c>, waiting for <c>ClientReady</c>) → <see cref="MatchPhase.Countdown"/> (ticks −CountdownTicks … −1:
    /// snapshots of the tick-0 state, inputs accepted but not applied, the simulation stands) → <see cref="MatchPhase.Running"/>
    /// (from T = 0: player inputs from <see cref="SlotInputBuffer"/>s → bots for fill and taken-over slots → input log →
    /// <c>MatchSimulation.Tick</c> → snapshot capture → snapshots) → <see cref="MatchPhase.Ended"/> (<c>MatchEnded</c> to
    /// everyone, the room lingers) → <see cref="MatchPhase.Closed"/>.
    /// <para>T (<see cref="Tick"/>) is the tick the server computes next; while running it equals <c>MatchState.Globals.Tick</c>
    /// and a snapshot of T shows the state after tick T − 1. Steps run on the host's schedule (start + n × 1/30 s).</para>
    /// Creating a room allocates; <see cref="Step"/> and the message handlers do not. One thread.
    /// </summary>
    public sealed class MatchRoom
    {
        private const long MicrosPerSecond = 1000000L;

        private readonly ServerSettings _settings;
        private readonly ServerTransport _transport;
        private readonly ServerMetrics _metrics;
        private readonly RoomSlot[] _slots;
        private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];
        private readonly byte[] _packet = new byte[NetConnection.MaxPayloadBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly MatchStartMessage _start = new MatchStartMessage();
        private readonly int _mapIndex;
        private readonly int _countdownTicks;
        private readonly int _divisor;
        private readonly long _startMicros;
        private long _steps;
        private int _phaseSteps;

        public MatchRoom(int index, ulong matchId, MatchRoomRequest request, NetToken[] tokens, ServerSettings settings,
            ServerTransport transport, ServerMetrics metrics, BotBehaviourDef behaviour, BotDifficultyDef botDifficulty, long nowMicros)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.Validate();
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            if (tokens == null || tokens.Length < request.Setup.SlotCount) throw new ArgumentException("MatchRoom: a token per slot");
            Index = index;
            MatchId = matchId;
            Defs = request.Defs;
            Setup = request.Setup;
            Mode = request.Mode;
            _mapIndex = request.MapIndex;
            _countdownTicks = settings.CountdownTicks;
            _divisor = settings.Snapshots.SnapshotRateDivisor;
            _startMicros = nowMicros;

            Simulation = new MatchSimulation(Defs, Setup);
            var bots = new BotController(behaviour);
            Takeover = new BotTakeover(bots, botDifficulty, Setup.Seed, settings.BotTakeoverTicks, settings.AfkTicks);
            Builder = new SnapshotBuilder(Defs, settings.Snapshots);
            if (settings.RecordInputLog) InputLog = new InputLog(Setup.SlotCount, LogCapacity(Defs));

            int count = Setup.SlotCount;
            _slots = new RoomSlot[count];
            for (int i = 0; i < count; i++)
            {
                bool player = request.PlayerSlots[i];
                _slots[i] = new RoomSlot(i, player, player ? tokens[i] : default, settings);
                _slots[i].Input.Reset(-_countdownTicks);
                Takeover.Set(i, player ? BotControlReason.Disconnected : BotControlReason.Fill);
            }
            Takeover.ResetCount();
            Result = new MatchEndedMessage();
            Tick = -_countdownTicks;
            Phase = MatchPhase.Joining;
        }

        public int Index { get; }
        public ulong MatchId { get; }
        public MatchDefs Defs { get; }
        public MatchSetup Setup { get; }
        public ModeKind Mode { get; }
        public MatchSimulation Simulation { get; }
        public BotTakeover Takeover { get; }
        public SnapshotBuilder Builder { get; }

        /// <summary>Every simulated input (null unless <c>RecordInputLog</c>).</summary>
        public InputLog InputLog { get; }

        public MatchPhase Phase { get; private set; }

        /// <summary>T — the tick the server computes next (negative in the countdown).</summary>
        public int Tick { get; private set; }

        public int SlotCount => _slots.Length;

        /// <summary>The result sent in <c>MatchEnded</c> (valid from <see cref="MatchPhase.Ended"/>).</summary>
        public MatchEndedMessage Result { get; }

        public RoomSlot GetSlot(int slot) => _slots[slot];

        /// <summary>Player slot holding <paramref name="token"/>, or −1.</summary>
        public int FindSlot(in NetToken token)
        {
            if (token.IsEmpty) return -1;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].IsPlayer && _slots[i].Token.Equals(token)) return i;
            return -1;
        }

        /// <summary>Steps due by <paramref name="nowMicros"/> (step n is due at start + n × 1/30 s; the error does not accumulate).</summary>
        public int TicksDue(long nowMicros)
        {
            long elapsed = nowMicros - _startMicros;
            if (elapsed <= 0) return 0;
            long due = elapsed * SimLimits.TickRate / MicrosPerSecond - _steps;
            return due <= 0 ? 0 : (due > int.MaxValue ? int.MaxValue : (int)due);
        }

        /// <summary>
        /// Clock time the next step is due (start + (n + 1) × 1/30 s, rounded up to a microsecond): the host process waits until
        /// the earliest of these (S3.9). Steps already due report a time in the past.
        /// </summary>
        public long NextStepMicros => _startMicros + ((_steps + 1) * MicrosPerSecond + SimLimits.TickRate - 1) / SimLimits.TickRate;

        /// <summary>Drops <paramref name="steps"/> due steps without running them (catch-up limit, §5.3).</summary>
        public void SkipSteps(int steps)
        {
            if (steps > 0) _steps += steps;
        }

        // ---- Connections and messages (from MatchHost) ----

        public void Bind(int slot, int connection)
        {
            RoomSlot s = _slots[slot];
            if (!s.IsConnected) _metrics.ConnectedPlayers++;
            if (s.EverConnected) _metrics.Rejoins++;
            s.EverConnected = true;
            s.Connection = connection;
            s.Ready = false;
            s.View.Reset();
            // A return after the end gets MatchStart too: a restarted client needs the slot table to show the result (S3.12).
            s.MatchStartPending = Phase >= MatchPhase.Loading;
            s.MatchEndedPending = Phase >= MatchPhase.Ended;
            s.EliminatedPending = s.EliminatedQueued && Phase < MatchPhase.Ended;
            Takeover.Clear(slot, BotControlReason.Disconnected);
        }

        public void Unbind(int slot)
        {
            RoomSlot s = _slots[slot];
            if (!s.IsConnected) return;
            _metrics.ConnectedPlayers--;
            s.Connection = -1;
            s.Ready = false;
            s.MatchStartPending = s.MatchEndedPending = s.EliminatedPending = false;
            Takeover.Set(slot, BotControlReason.Disconnected);
        }

        public void SetReady(int slot) => _slots[slot].Ready = true;

        /// <summary><c>Leave</c>: a bot takes over at once (§7).</summary>
        public void Leave(int slot)
        {
            if ((Takeover.ReasonsOf(slot) & BotControlReason.Left) != 0) return;
            _metrics.Leaves++;
            Takeover.Set(slot, BotControlReason.Left);
        }

        public void OnInput(int slot, InputMessage message)
        {
            if (Phase >= MatchPhase.Ended) return;
            SlotInputBuffer buffer = _slots[slot].Input;
            long stored = buffer.InputsStored, late = buffer.LateInputs, ahead = buffer.TooFarAhead;
            long carried = buffer.PressesCarried, dropped = buffer.PressesDropped;
            _metrics.InputPackets++;
            buffer.RecordPacket(message.NewestTick, Tick);
            for (int i = 0; i < message.Count; i++) buffer.Receive(message.TickAt(i), in message.Inputs[i], Tick);
            if (message.RequestKeyframe)
            {
                _slots[slot].View.RequestKeyframe();
                _metrics.KeyframeRequests++;
            }
            _metrics.InputsStored += buffer.InputsStored - stored;
            _metrics.LateInputs += buffer.LateInputs - late;
            _metrics.TooFarAheadInputs += buffer.TooFarAhead - ahead;
            _metrics.PressesCarried += buffer.PressesCarried - carried;
            _metrics.PressesDropped += buffer.PressesDropped - dropped;
        }

        public void OnPacketAcked(int slot, ushort sequence) => _slots[slot].View.OnPacketAcked(sequence);

        // ---- Tick ----

        /// <summary>One server step (1/30 s).</summary>
        public void Step(long nowMicros)
        {
            _steps++;
            _phaseSteps++;
            bool snapshot = false;
            switch (Phase)
            {
                case MatchPhase.Joining:
                    if (AllPlayersConnected() || _phaseSteps >= _settings.JoinTimeoutTicks) EnterLoading();
                    break;
                case MatchPhase.Loading:
                    if (AllConnectedReady() || _phaseSteps >= _settings.LoadTimeoutTicks) snapshot = EnterCountdown();
                    break;
                case MatchPhase.Countdown:
                    snapshot = StepCountdown();
                    break;
                case MatchPhase.Running:
                    snapshot = StepRunning();
                    break;
                case MatchPhase.Ended:
                    if (_phaseSteps >= _settings.ResultLingerTicks)
                    {
                        Phase = MatchPhase.Closed;
                        _metrics.MatchesClosed++;
                    }
                    break;
            }
            FlushReliable();
            if (snapshot) SendSnapshots(nowMicros);
        }

        private void EnterPhase(MatchPhase phase)
        {
            Phase = phase;
            _phaseSteps = 0;
        }

        private void EnterLoading()
        {
            EnterPhase(MatchPhase.Loading);
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].IsConnected) _slots[i].MatchStartPending = true;
        }

        private bool EnterCountdown()
        {
            EnterPhase(MatchPhase.Countdown);
            Builder.Capture(Simulation.State, null);
            if (Tick < 0) return false;
            EnterPhase(MatchPhase.Running);
            return true;
        }

        private bool StepCountdown()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].IsPlayer) _slots[i].Input.Skip(Tick);
            Tick++;
            if (Tick >= 0) EnterPhase(MatchPhase.Running);
            return IsSnapshotTick(Tick);
        }

        private bool StepRunning()
        {
            MatchState state = Simulation.State;
            int t = Tick;
            for (int i = 0; i < _slots.Length; i++)
            {
                RoomSlot s = _slots[i];
                if (!s.IsPlayer)
                {
                    _inputs[i] = default;
                    continue;
                }
                PlayerInput input = s.Input.Take(t, out bool real);
                if (s.IsConnected)
                {
                    if (real) _metrics.AppliedInputTicks++;
                    else _metrics.MissingInputTicks++;
                }
                long before = Takeover.TakeoverCount;
                Takeover.Observe(i, real, in input);
                _metrics.Takeovers += Takeover.TakeoverCount - before;
                _inputs[i] = input;
            }
            Takeover.Bots.Update(state, Defs, Simulation.Events, _inputs);
            InputLog?.Record(t, _inputs);
            Simulation.Tick(_inputs);
            _metrics.Ticks++;
            Tick = state.Globals.Tick;
            Builder.Capture(state, Simulation.Events);

            bool ended = Simulation.IsEnded;
            if (Mode == ModeKind.LastBeast && !ended) CheckEliminations(state);
            if (ended) EnterEnded(state);
            return ended || IsSnapshotTick(Tick);
        }

        private bool IsSnapshotTick(int tick)
        {
            int r = tick % _divisor;
            return r == 0;
        }

        private void CheckEliminations(MatchState state)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                RoomSlot s = _slots[i];
                if (!s.IsPlayer || s.EliminatedQueued || state.Stats[i].Place <= 0) continue;
                s.EliminatedQueued = true;
                s.EliminatedPending = s.IsConnected;
            }
        }

        private void EnterEnded(MatchState state)
        {
            EnterPhase(MatchPhase.Ended);
            _metrics.MatchesEnded++;
            MatchEndedMessage r = Result;
            r.EndTick = state.Mode.EndTick;
            r.WinnerTeam = state.Mode.WinnerTeam;
            r.MvpSlot = Mode == ModeKind.Acorns && Defs.Acorns != null ? MvpCalculator.Compute(state, Defs.Acorns) : FirstPlace(state);
            r.SlotCount = _slots.Length;
            for (int i = 0; i < _slots.Length; i++) r.Slots[i] = ResultOf(state, i);
            for (int i = 0; i < _slots.Length; i++)
            {
                RoomSlot s = _slots[i];
                s.EliminatedPending = false;
                if (s.IsConnected) s.MatchEndedPending = true;
            }
        }

        private int FirstPlace(MatchState state)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (state.Stats[i].Place == 1) return i;
            return -1;
        }

        private MatchSlotResult ResultOf(MatchState state, int slot)
        {
            ref FighterStats st = ref state.Stats[slot];
            return new MatchSlotResult
            {
                DamageDealt = st.DamageDealt,
                HealingDone = st.HealingDone,
                Kills = st.Kills,
                Deaths = st.Deaths,
                AcornsCollected = st.AcornsCollected,
                Place = st.Place,
                Afk = Takeover.WasAfk(slot)
            };
        }

        private bool AllPlayersConnected()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].IsPlayer && !_slots[i].IsConnected) return false;
            return true;
        }

        private bool AllConnectedReady()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].IsConnected && !_slots[i].Ready) return false;
            return true;
        }

        // ---- Sending ----

        private void FlushReliable()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                RoomSlot s = _slots[i];
                if (!s.IsConnected) continue;
                if (s.MatchStartPending)
                {
                    WriteMatchStart(i);
                    if (!SendReliable(s)) continue;
                    s.MatchStartPending = false;
                }
                if (s.EliminatedPending)
                {
                    var m = new EliminatedMessage { Tick = Tick, Slot = i, Result = ResultOf(Simulation.State, i) };
                    _writer.Reset(_packet, 0, NetLimits.MaxReliableMessageBytes);
                    m.Write(_writer);
                    if (!SendReliable(s)) continue;
                    s.EliminatedPending = false;
                }
                if (s.MatchEndedPending)
                {
                    _writer.Reset(_packet, 0, NetLimits.MaxReliableMessageBytes);
                    Result.Write(_writer);
                    if (!SendReliable(s)) continue;
                    s.MatchEndedPending = false;
                }
            }
        }

        private void WriteMatchStart(int slot)
        {
            MatchStartMessage m = _start;
            m.MatchId = MatchId;
            m.MapIndex = _mapIndex;
            m.Mode = Mode;
            m.OwnSlot = slot;
            m.CountdownTicks = _countdownTicks;
            m.SnapshotRateDivisor = _divisor;
            m.InputRedundancy = _settings.InputRedundancy;
            m.UnlockAllAbilities = Setup.UnlockAllAbilities;
            m.ShowBots = _settings.ShowBots;
            m.SlotCount = _slots.Length;
            for (int i = 0; i < _slots.Length; i++)
            {
                PlayerSlotSetup p = Setup.Slots[i];
                m.Slots[i] = new MatchSlotInfo
                {
                    FighterIndex = p.FighterIndex,
                    Team = p.Team,
                    Level = p.Level,
                    SpawnX = p.SpawnPosition.X,
                    SpawnY = p.SpawnPosition.Y,
                    IsBot = !_slots[i].IsPlayer
                };
            }
            _writer.Reset(_packet, 0, NetLimits.MaxReliableMessageBytes);
            m.Write(_writer);
        }

        private bool SendReliable(RoomSlot s)
        {
            if (_writer.IsOverflow) throw new InvalidOperationException("MatchRoom: reliable message exceeds MaxReliableMessageBytes");
            if (_transport.SendReliable(s.Connection, _packet, 0, _writer.BytesWritten)) return true;
            _metrics.ReliableSendFailures++;
            return false;
        }

        private void SendSnapshots(long nowMicros)
        {
            MatchState state = Simulation.State;
            for (int i = 0; i < _slots.Length; i++)
            {
                RoomSlot s = _slots[i];
                if (!s.IsConnected) continue;
                var header = new SnapshotHeader
                {
                    LastAppliedInputTick = s.Input.LastAppliedInputTick,
                    InputMarginMin = s.Input.InputMarginMin(Tick, _settings.MarginWindowTicks),
                    BotControlled = Takeover.IsBot(i)
                };
                _packet[0] = (byte)MessageKind.Snapshot;
                int bytes = Builder.Build(state, s.View, header, Tick, _packet, 1);
                if (bytes <= 0)
                {
                    _metrics.SnapshotsNotFitting++;
                    continue;
                }
                NetConnection c = _transport.GetConnection(s.Connection);
                ushort sequence = c.Acks.NextSequence;
                if (!_transport.Send(s.Connection, _packet, bytes + 1, nowMicros)) continue;
                s.View.MarkSent(Tick, sequence);
                _metrics.SnapshotsSent++;
                _metrics.SnapshotBytes += bytes;
                if (bytes > _metrics.MaxSnapshotBytes) _metrics.MaxSnapshotBytes = bytes;
                if (Builder.LastWasKeyframe) _metrics.Keyframes++;
            }
        }

        private static int LogCapacity(MatchDefs defs)
        {
            int ticks = 0;
            if (defs.Acorns != null) ticks = defs.Acorns.DurationTicks + defs.Acorns.HoldTicks;
            if (defs.LastBeast != null && defs.LastBeast.MaxDurationTicks > ticks) ticks = defs.LastBeast.MaxDurationTicks;
            return ticks + NetLimits.InputLogExtraTicks;
        }
    }
}
