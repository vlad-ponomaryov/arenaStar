using System;

namespace Arena.Net.Transport
{
    /// <summary>
    /// The server's connections (Server-Spec §3.2): up to <c>MaxConnections</c> slots, a dense list of active indices for
    /// iteration, a free stack and a <see cref="ConnectionIdMap"/>. A slot's <see cref="NetConnection"/> (with its 2 × 8 KB
    /// rings) is created the first time the slot is used and reused afterwards — memory grows with real connections, not with
    /// the limit. Lookup by address is a scan: it is needed only on handshake repeats.
    /// </summary>
    public sealed class ConnectionTable
    {
        private readonly NetConnection[] _slots;
        private readonly int[] _active;
        private readonly int[] _activePos;
        private readonly int[] _free;
        private int _freeCount;
        private readonly ConnectionIdMap _ids;

        public ConnectionTable(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _slots = new NetConnection[capacity];
            _active = new int[capacity];
            _activePos = new int[capacity];
            _free = new int[capacity];
            for (int i = 0; i < capacity; i++) _free[i] = capacity - 1 - i;
            _freeCount = capacity;
            _ids = new ConnectionIdMap(capacity);
        }

        public int Capacity { get; }

        public int ActiveCount { get; private set; }

        /// <summary>Index of the i-th active connection (0 ≤ i &lt; <see cref="ActiveCount"/>).</summary>
        public int ActiveAt(int i) => _active[i];

        public NetConnection Get(int index) => _slots[index];

        public bool ContainsId(ulong id) => _ids.Contains(id);

        public NetConnection FindById(ulong id) => _ids.TryGet(id, out int index) ? _slots[index] : null;

        public NetConnection FindByAddress(in NetAddress address)
        {
            for (int i = 0; i < ActiveCount; i++)
            {
                NetConnection c = _slots[_active[i]];
                if (c.State == ConnectionState.Connected && c.Address == address) return c;
            }
            return null;
        }

        /// <summary>Takes a free slot for a new connection with <paramref name="id"/>; null if the table is full.</summary>
        public NetConnection Allocate(ulong id)
        {
            if (_freeCount == 0 || id == 0ul || _ids.Contains(id)) return null;
            int index = _free[--_freeCount];
            NetConnection c = _slots[index] ?? (_slots[index] = new NetConnection(index));
            _ids.Add(id, index);
            _activePos[index] = ActiveCount;
            _active[ActiveCount++] = index;
            c.Id = id;
            return c;
        }

        public void Release(NetConnection connection)
        {
            int index = connection.Index;
            if (_slots[index] != connection || connection.State == ConnectionState.Free) return;
            _ids.Remove(connection.Id);
            int pos = _activePos[index];
            int last = _active[--ActiveCount];
            _active[pos] = last;
            _activePos[last] = pos;
            _free[_freeCount++] = index;
            connection.State = ConnectionState.Free;
            connection.Id = 0ul;
        }
    }
}
