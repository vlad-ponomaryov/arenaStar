using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;
using Arena.Net.Transport;

namespace Arena.Net.Client
{
    /// <summary>
    /// The client side of one online match (Server-Spec §2.3–2.5, §4.2, S3.7a) over a <see cref="NetClient"/>: reads
    /// <c>MatchStart</c> (Defs from the content pack — the same as the server's, D-083 — and the slot table), answers
    /// <c>ClientReady</c>, decodes snapshots into its ring (asks for a key frame when a baseline is missing), runs the prediction
    /// clock (<see cref="TimeSync"/>) and the own prediction (<see cref="Predictor"/>), the render clock (<see cref="InterpolationBuffer"/>)
    /// and the display state (<see cref="PresentationState"/>, built every prediction tick; server events by the
    /// <see cref="EventScheduler"/>), and sends an <c>Input</c> every prediction tick with the last <c>InputRedundancy</c> inputs not
    /// yet acknowledged by <c>LastAppliedInputTick</c>.
    /// Frame loop (as <c>IMatchDriver</c>, S3.10): <see cref="NetClient.Update"/> → <see cref="BeginFrame"/> (reconcile on a new
    /// snapshot, ticks due) → <see cref="Step"/> per tick. A tick predicted again after a clock jump back keeps the input already
    /// sent for it (the server keeps the first copy). Allocates only on <c>MatchStart</c> (Defs, shadow simulation).
    /// </summary>
    public sealed class MatchClientSession : IMatchMessageHandler
    {
        private const long MicrosPerSecond = 1000000L;

        private readonly ContentBundle _content;
        private readonly SnapshotDecoder _decoder = new SnapshotDecoder();
        private readonly NetSnapshotEvent[] _events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
        private readonly BitWriter _writer = new BitWriter();
        private readonly byte[] _send = new byte[NetConnection.MaxPayloadBytes];
        private readonly InputMessage _input = new InputMessage();
        private bool _readySent;
        private bool _readyWanted;
        private bool _requestKeyframe;
        private bool _newSnapshot;
        private long _lastFrameMicros;
        private bool _hasFrameTime;
        private bool _hasFirstTick;
        private int _connects;
        private EliminatedMessage _eliminated;

        public MatchClientSession(NetClient client, ContentBundle content, ClientMatchSettings settings = null)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            Settings = settings ?? ClientMatchSettings.CreateDefault();
            Clock = new TimeSync(Settings);
            Interpolation = new InterpolationBuffer(Settings, Ring);
            client.Match = this;
            _readyWanted = Settings.ReadyOnMatchStart;
        }

        public NetClient Client { get; }
        public ClientMatchSettings Settings { get; }
        public TimeSync Clock { get; }
        public InputHistory Inputs { get; } = new InputHistory();
        public SnapshotFrameRing Ring { get; } = new SnapshotFrameRing();
        public InterpolationBuffer Interpolation { get; }
        public EventScheduler Scheduler { get; } = new EventScheduler();
        public ClientNetStats NetStats { get; } = new ClientNetStats();

        /// <summary>The display state (created on <c>MatchStart</c>); read it after <see cref="Step"/>.</summary>
        public PresentationState Presentation { get; private set; }

        /// <summary>Created on <c>MatchStart</c>.</summary>
        public Predictor Predictor { get; private set; }

        public MatchDefs Defs { get; private set; }
        public MatchSetup Setup { get; private set; }

        public MatchStartMessage Start { get; } = new MatchStartMessage();
        public bool HasStart { get; private set; }

        public MatchEndedMessage Ended { get; } = new MatchEndedMessage();
        public bool HasEnded { get; private set; }

        public bool HasEliminated { get; private set; }
        public ref readonly EliminatedMessage Eliminated => ref _eliminated;

        public int OwnSlot => HasStart ? Start.OwnSlot : -1;

        /// <summary>The newest decoded view, or null.</summary>
        public SnapshotFrame Latest { get; private set; }

        public int NewestServerTick { get; private set; }

        /// <summary>The next tick to predict and send.</summary>
        public int PredictionTick => Clock.PredictionTick;

        /// <summary>Stop sending inputs while true (tests of the server's bot takeover).</summary>
        public bool Silent;

        public long SnapshotsDecoded { get; private set; }
        public long SnapshotsLate { get; private set; }
        public long Keyframes { get; private set; }
        public long MissingBaselines { get; private set; }
        public long MalformedSnapshots { get; private set; }
        public long InputPacketsSent { get; private set; }
        public long InputBytesSent { get; private set; }
        public long SnapshotBytesReceived { get; private set; }
        public long BotControlledSnapshots { get; private set; }

        /// <summary>The lowest tick of a decoded snapshot (negative when the countdown was seen).</summary>
        public int FirstServerTick { get; private set; }

        /// <summary>Server tick and <c>InputMarginMin</c> of the newest snapshot when the prediction tick reached 0 (clock check).</summary>
        public int MarginAtZero { get; private set; } = int.MinValue;

        /// <summary>A new connection replaced the old one this many times (rejoin, S3.12).</summary>
        public int Rejoins { get; private set; }

        /// <summary>The clocks were restarted after a pause of the app this many times (<see cref="OnResumed"/>).</summary>
        public int Resyncs { get; private set; }

        /// <summary>
        /// The app returns from a pause (<c>OnApplicationPause(false)</c>, Server-Spec §7): the time it was away is not caught up
        /// tick by tick — the prediction and render clocks and the own view restart from the next snapshot (a clock jump).
        /// If the connection died meanwhile, <see cref="NetClient"/> returns to the match by itself.
        /// </summary>
        public void OnResumed(long nowMicros)
        {
            _lastFrameMicros = nowMicros;
            if (!HasStart || HasEnded) return;
            ResetTimelines();
            Resyncs++;
        }

        /// <summary>Answer <c>MatchStart</c> with <c>ClientReady</c> (the scene is loaded).</summary>
        public void SetReady() => _readyWanted = true;

        public void Leave()
        {
            _send[0] = (byte)MessageKind.Leave;
            Client.SendReliable(_send, 0, 1);
        }

        /// <summary>
        /// Start of a frame (after <see cref="NetClient.Update"/>): sends <c>ClientReady</c> when due, reconciles the prediction
        /// with a new newest snapshot, decays the view correction. Returns how many ticks to <see cref="Step"/> this frame.
        /// </summary>
        public int BeginFrame(long nowMicros)
        {
            float seconds = _hasFrameTime ? (nowMicros - _lastFrameMicros) / (float)MicrosPerSecond : 0f;
            _lastFrameMicros = nowMicros;
            _hasFrameTime = true;
            if (HasEnded) Client.ReconnectEnabled = false; // nothing to return to (the result is here)
            SyncConnection();
            if (!Client.IsConnected) return 0;
            if (HasStart && _readyWanted && !_readySent)
            {
                _send[0] = (byte)MessageKind.ClientReady;
                _readySent = Client.SendReliable(_send, 0, 1);
            }
            if (_newSnapshot && Predictor != null && Latest != null)
            {
                _newSnapshot = false;
                Predictor.Reconcile(Latest, Inputs, Clock.PredictionTick, Presentation.HasState ? Presentation.State : null);
                if (Predictor.ResimLimitHit) Clock.JumpTo(Predictor.ShadowTick, Client.RttMicros);
            }
            Predictor?.Decay(seconds);
            if (HasEnded && Presentation != null && !Presentation.Ended) Presentation.ApplyEnded(Ended);
            if (HasEnded || !HasStart) return 0;
            return Clock.Advance(nowMicros);
        }

        /// <summary>
        /// One prediction tick with the collected (raw) own input: quantize, remember, predict, send. A tick sent before (clock
        /// jump back) keeps its first input.
        /// </summary>
        public void Step(in PlayerInput raw, long nowMicros)
        {
            int tick = Clock.PredictionTick;
            if (!Inputs.TryGet(tick, out PlayerInput input))
            {
                input = InputQuantizer.Apply(in raw);
                Inputs.Set(tick, in input);
            }
            if (tick == 0 && Latest != null) MarginAtZero = Latest.Header.InputMarginMin;
            bool predicted = false;
            if (Predictor != null)
            {
                if (Presentation.HasState) Predictor.SetShownOthers(Presentation.State);
                predicted = Predictor.Predict(tick, in input);
                Interpolation.Advance();
                Interpolation.Sample();
                Presentation.Build(tick, Predictor, Interpolation, Scheduler, predicted ? Predictor.Shadow.Events : null, NetStats);
            }
            if (!Silent) SendInput(tick, nowMicros);
            Clock.CompleteTick();
        }

        private void SendInput(int tick, long nowMicros)
        {
            int redundancy = Start.InputRedundancy;
            if (redundancy > NetLimits.MaxInputsPerPacket) redundancy = NetLimits.MaxInputsPerPacket;
            if (redundancy < 1) redundancy = 1;
            int acked = Latest != null ? Latest.Header.LastAppliedInputTick : int.MinValue;
            _input.NewestTick = tick;
            _input.RequestKeyframe = _requestKeyframe;
            _input.ViewDelayTicks = 0;
            int n = 0;
            for (int i = 0; i < redundancy; i++)
            {
                int t = tick - i;
                if (i > 0 && t <= acked) break;
                if (!Inputs.TryGet(t, out PlayerInput stored)) break;
                _input.Inputs[n++] = stored;
            }
            _input.Count = n;
            _writer.Reset(_send, 0, _send.Length);
            _input.Write(_writer);
            if (!Client.Send(_send, _writer.BytesWritten, nowMicros)) return;
            InputPacketsSent++;
            InputBytesSent += _writer.BytesWritten;
            _requestKeyframe = false;
        }

        // ---- IMatchMessageHandler ----

        public bool OnMatchMessage(MessageKind kind, BitReader reader)
        {
            SyncConnection();
            switch (kind)
            {
                case MessageKind.MatchStart:
                    if (HasStart) return Start.Read(reader); // a repeat (rejoin, S3.12): the match is already set up
                    if (!Start.Read(reader)) return false;
                    return SetUpMatch();
                case MessageKind.MatchEnded:
                    HasEnded = Ended.Read(reader);
                    return HasEnded;
                case MessageKind.Eliminated:
                    HasEliminated = _eliminated.Read(reader);
                    return HasEliminated;
                default:
                    return false;
            }
        }

        public bool OnSnapshot(byte[] buffer, int offset, int length)
        {
            SyncConnection();
            SnapshotBytesReceived += length;
            SnapshotDecodeStatus status = _decoder.Decode(buffer, offset, length, Ring, _events, out int eventCount,
                out SnapshotFrame frame, out int serverTick);
            switch (status)
            {
                case SnapshotDecodeStatus.Ok:
                    SnapshotsDecoded++;
                    if (frame.Header.IsKeyframe) Keyframes++;
                    if (HasStart) Scheduler.Add(_events, eventCount, Start.OwnSlot, !frame.Header.BotControlled);
                    if (Latest != null && unchecked(serverTick - NewestServerTick) <= 0)
                    {
                        SnapshotsLate++;
                        return true;
                    }
                    if (!_hasFirstTick)
                    {
                        FirstServerTick = serverTick;
                        _hasFirstTick = true;
                    }
                    Latest = frame;
                    NewestServerTick = serverTick;
                    if (frame.Header.BotControlled) BotControlledSnapshots++;
                    long rtt = Client.RttMicros;
                    long arrival = Client.Transport.Connection.LastReceiveMicros;
                    if (!Clock.Started) Clock.Start(serverTick, rtt, arrival);
                    else Clock.OnSnapshot(serverTick, frame.Header.InputMarginMin, rtt);
                    Interpolation.OnSnapshot(serverTick, arrival);
                    _newSnapshot = true;
                    return true;
                case SnapshotDecodeStatus.MissingBaseline:
                    MissingBaselines++;
                    _requestKeyframe = true;
                    return true;
                case SnapshotDecodeStatus.Duplicate:
                    return true;
                default:
                    MalformedSnapshots++;
                    return false;
            }
        }

        /// <summary>
        /// A new connection replaced the old one (rejoin, Server-Spec §7, §2.3): its first snapshot is a key frame on a new
        /// baseline chain, so the ring and the newest view are dropped, the clocks and the own view restart from it, pending
        /// events are forgotten and <c>ClientReady</c> goes out again. The input history stays (the server keeps the first copy
        /// of a tick).
        /// </summary>
        private void SyncConnection()
        {
            int connects = Client.Transport.Connects;
            if (connects == _connects) return;
            bool rejoin = _connects != 0;
            _connects = connects;
            if (!rejoin) return;
            Rejoins++;
            Ring.Clear();
            Latest = null;
            _requestKeyframe = false;
            _readySent = false;
            ResetTimelines();
        }

        private void ResetTimelines()
        {
            _newSnapshot = false;
            Clock.Reset();
            Interpolation.Reset();
            Scheduler.Clear();
            Predictor?.ResetView();
        }

        private bool SetUpMatch()
        {
            MatchStartMessage m = Start;
            var slots = new PlayerSlotSetup[m.SlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                MatchSlotInfo s = m.Slots[i];
                slots[i] = new PlayerSlotSetup(s.FighterIndex, s.Team, s.Level, new Vec2(s.SpawnX, s.SpawnY));
            }
            // The match seed is not sent: prediction never touches the match RNG (§2.5).
            var setup = new MatchSetup { Seed = 0, Slots = slots, UnlockAllAbilities = m.UnlockAllAbilities };
            try
            {
                MatchDefs defs = _content.CreateMatchDefs(m.MapIndex, m.Mode);
                Predictor = new Predictor(defs, setup, m.OwnSlot, Settings); // validates the slot table against the Defs
                Presentation = new PresentationState(defs, setup, m.OwnSlot, Settings);
                Defs = defs;
            }
            catch (ArgumentException)
            {
                return false; // not our content or a broken table: the match cannot be shown
            }
            Setup = setup;
            Interpolation.SnapshotInterval = m.SnapshotRateDivisor > 0 ? m.SnapshotRateDivisor : 1;
            HasStart = true;
            return true;
        }
    }
}
