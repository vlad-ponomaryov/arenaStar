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
    /// clock (<see cref="TimeSync"/>) and the own prediction (<see cref="Predictor"/>), and sends an <c>Input</c> every prediction
    /// tick with the last <c>InputRedundancy</c> inputs not yet acknowledged by <c>LastAppliedInputTick</c>.
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
        private EliminatedMessage _eliminated;

        public MatchClientSession(NetClient client, ContentBundle content, ClientMatchSettings settings = null)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            Settings = settings ?? ClientMatchSettings.CreateDefault();
            Clock = new TimeSync(Settings);
            client.Match = this;
            _readyWanted = Settings.ReadyOnMatchStart;
        }

        public NetClient Client { get; }
        public ClientMatchSettings Settings { get; }
        public TimeSync Clock { get; }
        public InputHistory Inputs { get; } = new InputHistory();
        public SnapshotFrameRing Ring { get; } = new SnapshotFrameRing();

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
            if (!Client.IsConnected) return 0;
            if (HasStart && _readyWanted && !_readySent)
            {
                _send[0] = (byte)MessageKind.ClientReady;
                _readySent = Client.SendReliable(_send, 0, 1);
            }
            if (_newSnapshot && Predictor != null && Latest != null)
            {
                _newSnapshot = false;
                Predictor.Reconcile(Latest, Inputs, Clock.PredictionTick);
                if (Predictor.ResimLimitHit) Clock.JumpTo(Predictor.ShadowTick, Client.RttMicros);
            }
            Predictor?.Decay(seconds);
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
            Predictor?.Predict(tick, in input);
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
            SnapshotBytesReceived += length;
            SnapshotDecodeStatus status = _decoder.Decode(buffer, offset, length, Ring, _events, out _, out SnapshotFrame frame,
                out int serverTick);
            switch (status)
            {
                case SnapshotDecodeStatus.Ok:
                    SnapshotsDecoded++;
                    if (frame.Header.IsKeyframe) Keyframes++;
                    if (Latest != null && unchecked(serverTick - NewestServerTick) <= 0)
                    {
                        SnapshotsLate++;
                        return true;
                    }
                    if (Latest == null) FirstServerTick = serverTick;
                    Latest = frame;
                    NewestServerTick = serverTick;
                    if (frame.Header.BotControlled) BotControlledSnapshots++;
                    long rtt = Client.RttMicros;
                    if (!Clock.Started) Clock.Start(serverTick, rtt, Client.Transport.Connection.LastReceiveMicros);
                    else Clock.OnSnapshot(serverTick, frame.Header.InputMarginMin, rtt);
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
                Defs = defs;
            }
            catch (ArgumentException)
            {
                return false; // not our content or a broken table: the match cannot be shown
            }
            Setup = setup;
            HasStart = true;
            return true;
        }
    }
}
