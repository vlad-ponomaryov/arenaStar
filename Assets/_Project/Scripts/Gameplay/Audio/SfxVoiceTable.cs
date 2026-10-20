using System;

namespace Arena.Gameplay.Audio
{
    /// <summary>
    /// Voice limit of the <see cref="SfxPlayer"/> pool (pure logic, testable without audio): a voice is busy until its clip ends.
    /// A new sound takes a free voice; if none, it replaces the oldest voice of the lowest priority, provided that priority is
    /// not above its own; otherwise it is dropped. No allocations after construction.
    /// </summary>
    public sealed class SfxVoiceTable
    {
        private readonly float[] _start;
        private readonly float[] _end;
        private readonly int[] _priority;

        public SfxVoiceTable(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _start = new float[capacity];
            _end = new float[capacity];
            _priority = new int[capacity];
            Reset();
        }

        public int Capacity => _end.Length;

        /// <summary>Voices still playing at <paramref name="now"/>.</summary>
        public int ActiveCount(float now)
        {
            int n = 0;
            for (int i = 0; i < _end.Length; i++)
                if (now < _end[i]) n++;
            return n;
        }

        /// <summary>Index of the voice for a new sound of <paramref name="duration"/> seconds, or −1 (dropped).</summary>
        public int Acquire(int priority, float now, float duration)
        {
            int pick = -1;
            for (int i = 0; i < _end.Length; i++)
            {
                if (now >= _end[i])
                {
                    pick = i;
                    break;
                }
            }
            if (pick < 0)
            {
                int victim = 0;
                for (int i = 1; i < _end.Length; i++)
                {
                    if (_priority[i] < _priority[victim] || (_priority[i] == _priority[victim] && _start[i] < _start[victim]))
                        victim = i;
                }
                if (_priority[victim] > priority) return -1;
                pick = victim;
            }
            _start[pick] = now;
            _end[pick] = now + (duration > 0f ? duration : 0f);
            _priority[pick] = priority;
            return pick;
        }

        /// <summary>All voices free.</summary>
        public void Reset()
        {
            for (int i = 0; i < _end.Length; i++)
            {
                _start[i] = float.NegativeInfinity;
                _end[i] = float.NegativeInfinity;
                _priority[i] = int.MinValue;
            }
        }
    }
}
