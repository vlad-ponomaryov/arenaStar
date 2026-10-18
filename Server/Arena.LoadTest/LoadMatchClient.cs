using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Net;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Diagnostics;
using Arena.Net.Snapshots;
using Arena.Net.Transport;

namespace Arena.LoadTest
{
    /// <summary>
    /// The match side of a light load client (Server-Spec §11.2, S3.15) over a <see cref="NetClient"/>: answers
    /// <c>MatchStart</c> with <c>ClientReady</c>, decodes snapshots (so baselines and key-frame requests behave as in the game),
    /// keeps its input tick at the server's <c>InputMarginMin</c> like <see cref="ScriptedMatchClient"/> and sends an
    /// <c>Input</c> with redundancy every client tick — a wandering, attacking player. No prediction, interpolation or views:
    /// the client is cheap so that one PC can drive hundreds of them against one server.
    /// </summary>
    public sealed class LoadMatchClient : IMatchMessageHandler
    {
        private const long MicrosPerSecond = 1000000L;
        private const int HistoryMask = NetLimits.InputBufferTicks - 1;
        private const int WanderPeriodTicks = 45;
        private const int AttackPeriodTicks = 23;
        private const int TrickPeriodTicks = 211;
        private const float WanderX = 0.7f;
        private const float WanderY = 0.6f;

        private readonly NetClient _net;
        private readonly int _salt;
        private readonly ScriptedClientSettings _settings = ScriptedClientSettings.CreateDefault();
        private readonly SnapshotDecoder _decoder = new SnapshotDecoder();
        private readonly SnapshotFrameRing _ring = new SnapshotFrameRing();
        private readonly NetSnapshotEvent[] _events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
        private readonly BitWriter _writer = new BitWriter();
        private readonly byte[] _send = new byte[NetConnection.MaxPayloadBytes];
        private readonly InputMessage _input = new InputMessage();
        private readonly int[] _historyTick = new int[NetLimits.InputBufferTicks];
        private readonly PlayerInput[] _history = new PlayerInput[NetLimits.InputBufferTicks];
        private SnapshotFrame _latest;
        private bool _readySent;
        private bool _requestKeyframe;
        private bool _clockStarted;
        private long _clockStartMicros;
        private long _clientSteps;
        private int _lastAdjustTick;
        private int _skipTicks;
        private int _inputTick;

        public LoadMatchClient(NetClient net, int salt)
        {
            _net = net;
            _salt = salt;
            net.Match = this;
            for (int i = 0; i < _historyTick.Length; i++) _historyTick[i] = int.MinValue;
        }

        public MatchStartMessage Start { get; } = new MatchStartMessage();
        public bool HasStart { get; private set; }
        public bool HasEnded { get; private set; }
        public int NewestServerTick { get; private set; }
        public long SnapshotsDecoded { get; private set; }
        public long MissingBaselines { get; private set; }
        public long Malformed { get; private set; }
        public long InputPacketsSent { get; private set; }

        /// <summary>The match is running for this client (a snapshot of tick ≥ 0 arrived).</summary>
        public bool Running => _latest != null && NewestServerTick >= 0 && !HasEnded;

        public void Update(long nowMicros)
        {
            if (!_net.IsConnected) return;
            if (HasStart && !_readySent)
            {
                _send[0] = (byte)MessageKind.ClientReady;
                _readySent = _net.SendReliable(_send, 0, 1);
            }
            if (!_clockStarted || HasEnded) return;
            long due = (nowMicros - _clockStartMicros) * SimLimits.TickRate / MicrosPerSecond;
            while (_clientSteps < due)
            {
                _clientSteps++;
                ClientTick(nowMicros);
            }
        }

        private void ClientTick(long nowMicros)
        {
            if (_skipTicks > 0)
            {
                _skipTicks--;
                return;
            }
            AdjustClock();
            int tick = _inputTick++;
            PlayerInput input = Quantize.Input(Wander(tick));
            int h = tick & HistoryMask;
            _historyTick[h] = tick;
            _history[h] = input;

            int redundancy = Start.InputRedundancy;
            if (redundancy > NetLimits.MaxInputsPerPacket) redundancy = NetLimits.MaxInputsPerPacket;
            if (redundancy < 1) redundancy = 1;
            int acked = _latest != null ? _latest.Header.LastAppliedInputTick : int.MinValue;
            _input.NewestTick = tick;
            _input.RequestKeyframe = _requestKeyframe;
            _input.InterpDelayTicks = 0;
            int n = 0;
            for (int i = 0; i < redundancy; i++)
            {
                int t = tick - i;
                if (i > 0 && t <= acked) break;
                int k = t & HistoryMask;
                if (_historyTick[k] != t) break;
                _input.Inputs[n++] = _history[k];
            }
            _input.Count = n;
            _writer.Reset(_send, 0, _send.Length);
            _input.Write(_writer);
            if (_net.Send(_send, _writer.BytesWritten, nowMicros)) InputPacketsSent++;
            _requestKeyframe = false;
        }

        private PlayerInput Wander(int tick)
        {
            int phase = (tick / WanderPeriodTicks + _salt) & 7;
            float x = (phase & 1) == 0 ? WanderX : -WanderX;
            float y = (phase & 2) == 0 ? WanderY : -WanderY;
            InputButtons buttons = tick % AttackPeriodTicks == _salt % AttackPeriodTicks ? InputButtons.Attack | InputButtons.AutoAim : InputButtons.None;
            if (tick % TrickPeriodTicks == 0) buttons |= InputButtons.Trick;
            return new PlayerInput(new Vec2(x, y), Vec2.Zero, buttons);
        }

        private void AdjustClock()
        {
            if (_latest == null || _inputTick - _lastAdjustTick < _settings.AdjustIntervalTicks) return;
            int error = _latest.Header.InputMarginMin - _settings.TargetMarginTicks;
            if (error < 0)
            {
                _inputTick -= error;
                _lastAdjustTick = _inputTick;
            }
            else if (error > _settings.MarginSlackTicks)
            {
                _skipTicks = error - _settings.MarginSlackTicks;
                _lastAdjustTick = _inputTick;
            }
        }

        // ---- IMatchMessageHandler ----

        public bool OnMatchMessage(MessageKind kind, BitReader reader)
        {
            switch (kind)
            {
                case MessageKind.MatchStart:
                    HasStart = Start.Read(reader);
                    return HasStart;
                case MessageKind.MatchEnded:
                    HasEnded = true;
                    return true;
                case MessageKind.Eliminated:
                    return true;
                default:
                    return false;
            }
        }

        public bool OnSnapshot(byte[] buffer, int offset, int length)
        {
            SnapshotDecodeStatus status = _decoder.Decode(buffer, offset, length, _ring, _events, out _, out SnapshotFrame frame, out int serverTick);
            switch (status)
            {
                case SnapshotDecodeStatus.Ok:
                    SnapshotsDecoded++;
                    if (_latest != null && unchecked(serverTick - NewestServerTick) <= 0) return true;
                    if (_latest == null)
                    {
                        _inputTick = serverTick + _settings.InitialLeadTicks;
                        _lastAdjustTick = _inputTick;
                        _clockStarted = true;
                        _clockStartMicros = _net.Transport.Connection.LastReceiveMicros;
                        _clientSteps = 0;
                    }
                    _latest = frame;
                    NewestServerTick = serverTick;
                    return true;
                case SnapshotDecodeStatus.MissingBaseline:
                    MissingBaselines++;
                    _requestKeyframe = true;
                    return true;
                case SnapshotDecodeStatus.Duplicate:
                    return true;
                default:
                    Malformed++;
                    return false;
            }
        }
    }
}
