using Arena.Core.Input;

namespace Arena.Net.Client
{
    /// <summary>
    /// The client's quantized inputs by prediction tick (ring of <see cref="NetLimits.InputBufferTicks"/>, Server-Spec §2.4–2.5):
    /// the source of the redundant inputs of an <c>Input</c> packet and of the re-simulation after a snapshot. A tick without a
    /// stored input (never predicted, overwritten, or after <see cref="Clear"/>) reads as the zero input — what the server
    /// simulates for a missing input after the repeat window. Allocated once.
    /// </summary>
    public sealed class InputHistory
    {
        private const int Mask = NetLimits.InputBufferTicks - 1;

        private readonly int[] _tick = new int[NetLimits.InputBufferTicks];
        private readonly PlayerInput[] _input = new PlayerInput[NetLimits.InputBufferTicks];
        private readonly bool[] _used = new bool[NetLimits.InputBufferTicks];

        public int Capacity => _input.Length;

        public void Set(int tick, in PlayerInput input)
        {
            int i = tick & Mask;
            _tick[i] = tick;
            _input[i] = input;
            _used[i] = true;
        }

        public bool TryGet(int tick, out PlayerInput input)
        {
            int i = tick & Mask;
            if (_used[i] && _tick[i] == tick)
            {
                input = _input[i];
                return true;
            }
            input = default;
            return false;
        }

        /// <summary>The stored input of <paramref name="tick"/>, or the zero input.</summary>
        public PlayerInput Get(int tick)
        {
            TryGet(tick, out PlayerInput input);
            return input;
        }

        public void Clear()
        {
            for (int i = 0; i < _used.Length; i++) _used[i] = false;
        }
    }
}
