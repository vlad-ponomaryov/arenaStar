using System;
using Arena.Core.Input;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;
using Arena.Net.Transport;

namespace Arena.Net.Server.Diagnostics
{
    /// <summary>
    /// A minimal match client for server tests and the server self-test (Server-Spec §11, S3.6) — not the game client
    /// (<c>MatchClientSession</c>, S3.7a): connects with a slot ticket, reads <c>MatchStart</c> / <c>MatchEnded</c> /
    /// <c>Eliminated</c>, answers <c>ClientReady</c>, decodes snapshots into its ring (asks for a key frame when a baseline is
    /// missing) and every 1/30 s of its own clock sends an <c>Input</c> for its tick with the last <c>InputRedundancy</c> inputs.
    /// Its tick starts at the first snapshot tick + <see cref="ScriptedClientSettings.InitialLeadTicks"/> and is nudged by the
    /// server's <c>InputMarginMin</c> once per <see cref="ScriptedClientSettings.AdjustIntervalTicks"/> — a crude version of the
    /// real clock (§2.3). Inputs come from <see cref="Script"/> (null — zero input). No allocations after construction.
    /// </summary>
    public sealed class ScriptedMatchClient : IConnectionHandler
    {
        /// <summary>The input for client tick <paramref name="tick"/>; <paramref name="latest"/> — the newest decoded view (may be null).</summary>
        public delegate PlayerInput InputScript(int tick, SnapshotFrame latest);

        private const long MicrosPerSecond = 1000000L;
        private const int HistoryMask = NetLimits.InputBufferTicks - 1;

        private readonly ScriptedClientSettings _settings;
        private readonly SnapshotDecoder _decoder = new SnapshotDecoder();
        private readonly NetSnapshotEvent[] _events = new NetSnapshotEvent[NetLimits.MaxSnapshotEvents];
        private readonly BitReader _reader = new BitReader();
        private readonly BitWriter _writer = new BitWriter();
        private readonly byte[] _send = new byte[NetConnection.MaxPayloadBytes];
        private readonly InputMessage _input = new InputMessage();
        private readonly int[] _historyTick = new int[NetLimits.InputBufferTicks];
        private readonly PlayerInput[] _history = new PlayerInput[NetLimits.InputBufferTicks];
        private bool _readySent;
        private bool _requestKeyframe;
        private bool _clockStarted;
        private long _clockStartMicros;
        private long _clientSteps;
        private int _lastAdjustTick;
        private int _skipTicks;

        public ScriptedMatchClient(TransportSettings transport, ulong contentHash, INetRandom random, ScriptedClientSettings settings = null)
        {
            _settings = settings ?? ScriptedClientSettings.CreateDefault();
            Transport = new ClientTransport(transport, contentHash, random, this);
            for (int i = 0; i < _historyTick.Length; i++) _historyTick[i] = int.MinValue;
        }

        public ClientTransport Transport { get; }

        public InputScript Script;

        /// <summary>Stop sending inputs (the server's bot takeover) while true.</summary>
        public bool Silent;

        /// <summary>Do not answer <c>MatchStart</c> with <c>ClientReady</c> (load timeout tests).</summary>
        public bool WithholdReady;

        public SnapshotFrameRing Ring { get; } = new SnapshotFrameRing();

        public MatchStartMessage Start { get; } = new MatchStartMessage();
        public bool HasStart { get; private set; }

        public MatchEndedMessage Ended { get; } = new MatchEndedMessage();
        public bool HasEnded { get; private set; }

        public EliminatedMessage Eliminated { get; private set; }
        public bool HasEliminated { get; private set; }

        /// <summary>The newest decoded view (the newest server tick), or null.</summary>
        public SnapshotFrame Latest { get; private set; }

        public int NewestServerTick { get; private set; }

        /// <summary>The client's input tick (the next one to send).</summary>
        public int InputTick { get; private set; }

        public long SnapshotsDecoded { get; private set; }
        public long SnapshotsLate { get; private set; }
        public long Keyframes { get; private set; }
        public long MissingBaselines { get; private set; }
        public long Malformed { get; private set; }
        public long InputPacketsSent { get; private set; }
        public long BotControlledSnapshots { get; private set; }
        public long MaxSnapshotBytes { get; private set; }

        /// <summary>The lowest server tick of a decoded snapshot (−CountdownTicks + 1 … 0 when the countdown was seen).</summary>
        public int FirstServerTick { get; private set; }

        /// <summary>Called on every decoded (newest) view.</summary>
        public Action<SnapshotFrame> OnSnapshot;

        public void Connect(IDatagramSocket socket, in NetToken token, long nowMicros) =>
            Transport.Connect(socket, ConnectPurpose.Match, true, token, nowMicros);

        public void Leave()
        {
            _send[0] = (byte)MessageKind.Leave;
            Transport.SendReliable(_send, 0, 1);
        }

        public void Update(long nowMicros)
        {
            Transport.Update(nowMicros);
            if (!Transport.IsConnected) return;
            if (HasStart && !_readySent && !WithholdReady)
            {
                _send[0] = (byte)MessageKind.ClientReady;
                _readySent = Transport.SendReliable(_send, 0, 1);
            }
            if (!_clockStarted || HasEnded) return;
            long due = (nowMicros - _clockStartMicros) * Core.Sim.SimLimits.TickRate / MicrosPerSecond;
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
            int tick = InputTick++;
            PlayerInput input = Script != null ? Script(tick, Latest) : default;
            input = Quantize.Input(in input);
            int h = tick & HistoryMask;
            _historyTick[h] = tick;
            _history[h] = input;
            if (Silent) return;

            int redundancy = HasStart ? Start.InputRedundancy : 1;
            if (redundancy > NetLimits.MaxInputsPerPacket) redundancy = NetLimits.MaxInputsPerPacket;
            int acked = Latest != null ? Latest.Header.LastAppliedInputTick : int.MinValue;
            _input.NewestTick = tick;
            _input.RequestKeyframe = _requestKeyframe;
            _input.ViewDelayTicks = 0;
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
            if (Transport.Send(_send, _writer.BytesWritten, nowMicros)) InputPacketsSent++;
            _requestKeyframe = false;
        }

        private void AdjustClock()
        {
            if (Latest == null || InputTick - _lastAdjustTick < _settings.AdjustIntervalTicks) return;
            int error = Latest.Header.InputMarginMin - _settings.TargetMarginTicks;
            if (error < 0)
            {
                InputTick -= error; // too late: jump ahead
                _lastAdjustTick = InputTick;
            }
            else if (error > _settings.MarginSlackTicks)
            {
                _skipTicks = error - _settings.MarginSlackTicks; // too early: hold a few ticks
                _lastAdjustTick = InputTick;
            }
        }

        // ---- IConnectionHandler ----

        public void OnReliableMessage(int connection, byte[] buffer, int offset, int length)
        {
            if (length < 1) return;
            _reader.Reset(buffer, offset + 1, length - 1);
            switch ((MessageKind)buffer[offset])
            {
                case MessageKind.MatchStart:
                    HasStart = Start.Read(_reader);
                    if (!HasStart) Malformed++;
                    break;
                case MessageKind.MatchEnded:
                    HasEnded = Ended.Read(_reader);
                    if (!HasEnded) Malformed++;
                    break;
                case MessageKind.Eliminated:
                    var m = new EliminatedMessage();
                    HasEliminated = m.Read(_reader);
                    Eliminated = m;
                    if (!HasEliminated) Malformed++;
                    break;
                default:
                    Malformed++;
                    break;
            }
        }

        public void OnPayload(int connection, ushort sequence, byte[] buffer, int offset, int length)
        {
            if (length < 1 || (MessageKind)buffer[offset] != MessageKind.Snapshot)
            {
                Malformed++;
                return;
            }
            if (length - 1 > MaxSnapshotBytes) MaxSnapshotBytes = length - 1;
            SnapshotDecodeStatus status = _decoder.Decode(buffer, offset + 1, length - 1, Ring, _events, out _, out SnapshotFrame frame,
                out int serverTick);
            switch (status)
            {
                case SnapshotDecodeStatus.Ok:
                    SnapshotsDecoded++;
                    if (frame.Header.IsKeyframe) Keyframes++;
                    if (Latest != null && unchecked(serverTick - NewestServerTick) <= 0)
                    {
                        SnapshotsLate++;
                        return;
                    }
                    if (Latest == null)
                    {
                        FirstServerTick = serverTick;
                        InputTick = serverTick + _settings.InitialLeadTicks;
                        _lastAdjustTick = InputTick;
                        _clockStarted = true;
                        _clockStartMicros = Transport.Connection.LastReceiveMicros;
                        _clientSteps = 0;
                    }
                    Latest = frame;
                    NewestServerTick = serverTick;
                    if (frame.Header.BotControlled) BotControlledSnapshots++;
                    OnSnapshot?.Invoke(frame);
                    break;
                case SnapshotDecodeStatus.MissingBaseline:
                    MissingBaselines++;
                    _requestKeyframe = true;
                    break;
                case SnapshotDecodeStatus.Duplicate:
                    break;
                default:
                    Malformed++;
                    break;
            }
        }

        public void OnPacketAcked(int connection, ushort sequence) { }
    }
}
