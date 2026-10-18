using System;

namespace Arena.Net.Transport
{
    /// <summary>
    /// Connection id → connection index: open addressing with linear probing and backward-shift deletion, preallocated for
    /// twice the capacity (power of two). Id 0 is reserved ("no connection"). No allocations after construction.
    /// </summary>
    public sealed class ConnectionIdMap
    {
        private readonly ulong[] _keys;
        private readonly int[] _values;
        private readonly int _mask;

        public ConnectionIdMap(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            int size = 1;
            while (size < capacity * 2) size <<= 1;
            _keys = new ulong[size];
            _values = new int[size];
            _mask = size - 1;
        }

        public int Count { get; private set; }

        public bool TryGet(ulong id, out int index)
        {
            if (id != 0ul)
            {
                for (int s = Slot(id); _keys[s] != 0ul; s = (s + 1) & _mask)
                {
                    if (_keys[s] != id) continue;
                    index = _values[s];
                    return true;
                }
            }
            index = -1;
            return false;
        }

        public bool Contains(ulong id) => TryGet(id, out _);

        /// <summary>False if the id is 0 or already present.</summary>
        public bool Add(ulong id, int index)
        {
            if (id == 0ul) return false;
            int s = Slot(id);
            for (; _keys[s] != 0ul; s = (s + 1) & _mask)
                if (_keys[s] == id) return false;
            _keys[s] = id;
            _values[s] = index;
            Count++;
            return true;
        }

        public bool Remove(ulong id)
        {
            if (id == 0ul) return false;
            int s = Slot(id);
            while (_keys[s] != id)
            {
                if (_keys[s] == 0ul) return false;
                s = (s + 1) & _mask;
            }
            // Backward-shift deletion keeps every probe chain unbroken without tombstones.
            int hole = s;
            int next = (hole + 1) & _mask;
            while (_keys[next] != 0ul)
            {
                int home = Slot(_keys[next]);
                bool movable = hole <= next ? home <= hole || home > next : home <= hole && home > next;
                if (movable)
                {
                    _keys[hole] = _keys[next];
                    _values[hole] = _values[next];
                    hole = next;
                }
                next = (next + 1) & _mask;
            }
            _keys[hole] = 0ul;
            Count--;
            return true;
        }

        private int Slot(ulong id)
        {
            ulong h = id * 0x9E3779B97F4A7C15ul;
            return (int)(h >> 40) & _mask;
        }
    }
}
