using Arena.Core.Input;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// The server's input buffer of one slot (Server-Spec §2.3, §2.4): a ring by tick of <see cref="NetLimits.InputBufferTicks"/>.
    /// <list type="bullet">
    /// <item>An input for tick t with T ≤ t ≤ T + <c>MaxInputAheadTicks</c> is stored — the first copy wins; farther ahead it is
    /// dropped and counted.</item>
    /// <item><see cref="Take"/> gives the input of tick T: the real one, else the last real move/aim <b>without buttons</b> for
    /// up to <c>MissingInputRepeatTicks</c>, then a zero input.</item>
    /// <item>A late copy (t &lt; T) of a tick that was simulated without a real input loses its movement, but its presses (attack,
    /// ult, trick with their aim and <c>AutoAim</c>) go to a queue of <see cref="NetLimits.MaxPendingPresses"/> and are applied
    /// at the next tick where they make a rising edge: the button was not in the previously applied tick and the tick has no
    /// other aimed press (one <c>PlayerInput.Aim</c>; attack before ult, as the input collector does). Presses are not lost (D-006).</item>
    /// <item>Countdown ticks (&lt; 0) are <see cref="Skip"/>ped: accepted for the margin, never applied (§2.3).</item>
    /// <item>The margin of every input packet (newest tick − T) is kept per server tick; <see cref="InputMarginMin"/> is the
    /// minimum over the window.</item>
    /// </list>
    /// The rising edge itself is still found by the simulation (<c>PrevButtons</c>), so a repeat without buttons makes no false
    /// press. One thread, no allocations.
    /// </summary>
    public sealed class SlotInputBuffer
    {
        private const int Mask = NetLimits.InputBufferTicks - 1;
        private const int MarginMask = NetLimits.MarginHistoryTicks - 1;

        private readonly int[] _tick = new int[NetLimits.InputBufferTicks];
        private readonly InputTickState[] _state = new InputTickState[NetLimits.InputBufferTicks];
        private readonly PlayerInput[] _input = new PlayerInput[NetLimits.InputBufferTicks];
        private readonly PendingPress[] _pending = new PendingPress[NetLimits.MaxPendingPresses];
        private readonly int[] _marginTick = new int[NetLimits.MarginHistoryTicks];
        private readonly int[] _marginValue = new int[NetLimits.MarginHistoryTicks];
        private readonly bool[] _marginUsed = new bool[NetLimits.MarginHistoryTicks];

        private int _maxAhead;
        private int _repeatTicks;
        private int _pendingCount;
        private PlayerInput _lastReal;
        private int _sinceReal;
        private InputButtons _lastAppliedButtons;
        private int _lastMarginMin;

        public SlotInputBuffer(int maxInputAheadTicks, int missingInputRepeatTicks)
        {
            Configure(maxInputAheadTicks, missingInputRepeatTicks);
            Reset(0);
        }

        /// <summary>Newest tick whose real input was applied (or accepted in the countdown) — the input acknowledgement (§2.4).</summary>
        public int LastAppliedInputTick { get; private set; }

        /// <summary>A real input was received since <see cref="Reset"/>.</summary>
        public bool HasReceived { get; private set; }

        public int PendingCount => _pendingCount;

        // ---- Counters (metrics, tests) ----
        public long InputsStored { get; private set; }
        public long Duplicates { get; private set; }
        public long TooFarAhead { get; private set; }
        public long TooOld { get; private set; }
        public long LateInputs { get; private set; }
        public long PressesCarried { get; private set; }
        public long PressesApplied { get; private set; }
        public long PressesDropped { get; private set; }

        /// <summary>Simulated ticks without a real input (repeat or zero).</summary>
        public long MissingTicks { get; private set; }
        public long AppliedTicks { get; private set; }

        public void Configure(int maxInputAheadTicks, int missingInputRepeatTicks)
        {
            if (maxInputAheadTicks < 1 || maxInputAheadTicks >= NetLimits.InputBufferTicks / 2)
                throw new System.ArgumentOutOfRangeException(nameof(maxInputAheadTicks));
            _maxAhead = maxInputAheadTicks;
            _repeatTicks = missingInputRepeatTicks < 0 ? 0 : missingInputRepeatTicks;
        }

        /// <summary>Forgets everything; the first tick to be taken or skipped is <paramref name="firstTick"/>.</summary>
        public void Reset(int firstTick)
        {
            for (int i = 0; i < _tick.Length; i++)
            {
                _tick[i] = int.MinValue;
                _state[i] = InputTickState.Empty;
                _input[i] = default;
            }
            for (int i = 0; i < _marginUsed.Length; i++) _marginUsed[i] = false;
            _pendingCount = 0;
            _lastReal = default;
            _sinceReal = int.MaxValue / 2;
            _lastAppliedButtons = InputButtons.None;
            _lastMarginMin = 0;
            LastAppliedInputTick = firstTick - 1;
            HasReceived = false;
            InputsStored = Duplicates = TooFarAhead = TooOld = LateInputs = 0;
            PressesCarried = PressesApplied = PressesDropped = MissingTicks = AppliedTicks = 0;
        }

        /// <summary>Notes the margin of an input packet whose newest tick is <paramref name="newestTick"/>, received while T = <paramref name="serverTick"/>.</summary>
        public void RecordPacket(int newestTick, int serverTick)
        {
            int margin = newestTick - serverTick;
            int i = serverTick & MarginMask;
            if (!_marginUsed[i] || _marginTick[i] != serverTick)
            {
                _marginUsed[i] = true;
                _marginTick[i] = serverTick;
                _marginValue[i] = margin;
            }
            else if (margin < _marginValue[i]) _marginValue[i] = margin;
        }

        /// <summary>Minimum packet margin over the last <paramref name="windowTicks"/> server ticks up to T; none — the last value.</summary>
        public int InputMarginMin(int serverTick, int windowTicks)
        {
            if (windowTicks > NetLimits.MarginHistoryTicks) windowTicks = NetLimits.MarginHistoryTicks;
            bool any = false;
            int min = 0;
            for (int k = 0; k < windowTicks; k++)
            {
                int t = serverTick - k;
                int i = t & MarginMask;
                if (!_marginUsed[i] || _marginTick[i] != t) continue;
                if (!any || _marginValue[i] < min) min = _marginValue[i];
                any = true;
            }
            if (any) _lastMarginMin = min;
            return _lastMarginMin;
        }

        /// <summary>One input of a packet: for tick <paramref name="tick"/>, received while T = <paramref name="serverTick"/>.</summary>
        public void Receive(int tick, in PlayerInput input, int serverTick)
        {
            int ahead = tick - serverTick;
            if (ahead > _maxAhead)
            {
                TooFarAhead++;
                return;
            }
            int i = tick & Mask;
            if (ahead >= 0)
            {
                if (_tick[i] == tick && _state[i] == InputTickState.Received)
                {
                    Duplicates++;
                    return;
                }
                _tick[i] = tick;
                _state[i] = InputTickState.Received;
                _input[i] = input;
                InputsStored++;
                HasReceived = true;
                return;
            }
            if (_tick[i] != tick)
            {
                TooOld++;
                return;
            }
            switch (_state[i])
            {
                case InputTickState.Missed:
                    _state[i] = InputTickState.LateHandled;
                    LateInputs++;
                    HasReceived = true;
                    CarryPresses(tick, in input);
                    break;
                default:
                    Duplicates++;
                    break;
            }
        }

        /// <summary>A countdown tick passes: its input (if any) is consumed and not applied.</summary>
        public void Skip(int tick)
        {
            int i = tick & Mask;
            if (_tick[i] == tick && _state[i] == InputTickState.Received)
            {
                if (tick > LastAppliedInputTick) LastAppliedInputTick = tick;
            }
            _tick[i] = tick;
            _state[i] = InputTickState.Skipped;
        }

        /// <summary>
        /// The input to simulate for tick <paramref name="tick"/> (call once per tick, in order). <paramref name="real"/> — the
        /// client's own input for this tick arrived in time.
        /// </summary>
        public PlayerInput Take(int tick, out bool real)
        {
            int i = tick & Mask;
            PlayerInput input;
            if (_tick[i] == tick && _state[i] == InputTickState.Received)
            {
                input = _input[i];
                real = true;
                _state[i] = InputTickState.Applied;
                _lastReal = input;
                _sinceReal = 0;
                LastAppliedInputTick = tick;
                AppliedTicks++;
            }
            else
            {
                real = false;
                _sinceReal++;
                input = _sinceReal <= _repeatTicks ? new PlayerInput(_lastReal.Move, _lastReal.Aim, InputButtons.None) : default;
                _tick[i] = tick;
                _state[i] = InputTickState.Missed;
                MissingTicks++;
            }
            if (_pendingCount > 0) ApplyPending(ref input);
            _lastAppliedButtons = input.Buttons;
            return input;
        }

        public PendingPress GetPending(int index) => _pending[index];

        private void CarryPresses(int tick, in PlayerInput input)
        {
            InputButtons auto = input.Buttons & InputButtons.AutoAim;
            if ((input.Buttons & InputButtons.Attack) != 0) Enqueue(InputButtons.Attack | auto, input, tick);
            if ((input.Buttons & InputButtons.Ult) != 0) Enqueue(InputButtons.Ult | auto, input, tick);
            if ((input.Buttons & InputButtons.Trick) != 0) Enqueue(InputButtons.Trick, input, tick);
        }

        private void Enqueue(InputButtons buttons, in PlayerInput input, int tick)
        {
            if (_pendingCount >= _pending.Length)
            {
                PressesDropped++;
                return;
            }
            _pending[_pendingCount++] = new PendingPress { Buttons = buttons, Aim = input.Aim, Tick = tick };
            PressesCarried++;
        }

        private void ApplyPending(ref PlayerInput input)
        {
            const InputButtons aimed = InputButtons.Attack | InputButtons.Ult;
            bool aimTaken = (input.Buttons & aimed) != 0;
            int k = 0;
            while (k < _pendingCount)
            {
                PendingPress p = _pending[k];
                InputButtons button = p.Button;
                bool edge = (input.Buttons & button) == 0 && (_lastAppliedButtons & button) == 0;
                if (p.IsAimed && aimTaken) edge = false;
                if (!edge)
                {
                    k++;
                    continue;
                }
                if (p.IsAimed)
                {
                    input.Buttons = (input.Buttons & ~InputButtons.AutoAim) | p.Buttons;
                    input.Aim = p.Aim;
                    aimTaken = true;
                }
                else input.Buttons |= p.Buttons;
                PressesApplied++;
                for (int j = k + 1; j < _pendingCount; j++) _pending[j - 1] = _pending[j];
                _pendingCount--;
            }
        }
    }
}
