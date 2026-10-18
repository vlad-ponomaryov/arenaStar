namespace Arena.Core.Sim.Events
{
    /// <summary>
    /// Events of the last tick (D-008): fixed-capacity array (<see cref="SimLimits.MaxEvents"/>), cleared at the start of
    /// every tick, read by views after the tick. Not part of the snapshot. Overflow drops the event and counts it.
    /// </summary>
    public sealed class SimEventBuffer
    {
        private readonly SimEvent[] _items = new SimEvent[SimLimits.MaxEvents];

        public int Count { get; private set; }

        /// <summary>Events dropped because the buffer was full (since the last <see cref="Clear"/>).</summary>
        public int Dropped { get; private set; }

        public int Capacity => _items.Length;

        public ref readonly SimEvent this[int index] => ref _items[index];

        public void Clear()
        {
            Count = 0;
            Dropped = 0;
        }

        public bool Add(in SimEvent e)
        {
            if (Count >= _items.Length)
            {
                Dropped++;
                return false;
            }
            _items[Count++] = e;
            return true;
        }

        /// <summary>Number of events of the given type in the buffer (tests, debug).</summary>
        public int CountOf(SimEventType type)
        {
            int n = 0;
            for (int i = 0; i < Count; i++)
                if (_items[i].Type == type) n++;
            return n;
        }
    }
}
