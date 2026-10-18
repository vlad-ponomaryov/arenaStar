using System;
using Arena.Core.Modes;

namespace Arena.Net.Server.Lobby
{
    /// <summary>
    /// The FIFO queue of one mode on this node (Server-Spec §6.2: a queue per mode and per node; region = node): connection
    /// indices in join order, preallocated for every connection of the transport. Removal keeps the order. No allocations.
    /// </summary>
    public sealed class ModeQueue
    {
        private readonly int[] _connections;

        public ModeQueue(ModeKind mode, int capacity, int teamSize, int maxTickets)
        {
            if (capacity <= 0) throw new ArgumentException("ModeQueue: capacity must be > 0");
            if (maxTickets <= 0) throw new ArgumentException("ModeQueue: maxTickets must be > 0");
            Mode = mode;
            Capacity = capacity;
            TeamSize = teamSize;
            _connections = new int[maxTickets];
        }

        public ModeKind Mode { get; }

        /// <summary>Slots of a match of this mode (players + bots).</summary>
        public int Capacity { get; }

        /// <summary>Slots per team; 0 — free-for-all.</summary>
        public int TeamSize { get; }

        public int Count { get; private set; }

        /// <summary>Connection of the i-th ticket in join order (0 — the oldest).</summary>
        public int At(int i) => _connections[i];

        public void Add(int connection)
        {
            if (Count == _connections.Length) throw new InvalidOperationException("ModeQueue: full");
            _connections[Count++] = connection;
        }

        public bool Remove(int connection)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_connections[i] != connection) continue;
                Array.Copy(_connections, i + 1, _connections, i, Count - i - 1);
                Count--;
                return true;
            }
            return false;
        }

        /// <summary>Drops the <paramref name="count"/> oldest tickets.</summary>
        public void RemoveFront(int count)
        {
            if (count <= 0) return;
            if (count > Count) count = Count;
            Array.Copy(_connections, count, _connections, 0, Count - count);
            Count -= count;
        }
    }
}
