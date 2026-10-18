using System;

namespace Arena.Online
{
    /// <summary>Datagrams of the <see cref="LagSimulatorSocket"/> waiting for their due time in preallocated slots; the
    /// earliest due one leaves first. No allocations after construction.</summary>
    internal sealed class DelayQueue
    {
        private readonly byte[][] _data;
        private readonly int[] _length;
        private readonly long[] _due;
        private readonly bool[] _used;

        public DelayQueue(int slots, int bytes)
        {
            _data = new byte[slots][];
            for (int i = 0; i < slots; i++) _data[i] = new byte[bytes];
            _length = new int[slots];
            _due = new long[slots];
            _used = new bool[slots];
        }

        /// <summary>False when every slot is taken (the datagram is dropped).</summary>
        public bool Push(byte[] buffer, int offset, int length, long due)
        {
            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i] || length > _data[i].Length) continue;
                Buffer.BlockCopy(buffer, offset, _data[i], 0, length);
                _length[i] = length;
                _due[i] = due;
                _used[i] = true;
                return true;
            }
            return false;
        }

        public bool PopDue(long now, byte[] destination, out int length)
        {
            int best = -1;
            for (int i = 0; i < _used.Length; i++)
                if (_used[i] && _due[i] <= now && (best < 0 || _due[i] < _due[best])) best = i;
            length = 0;
            if (best < 0) return false;
            length = _length[best];
            Buffer.BlockCopy(_data[best], 0, destination, 0, length);
            _used[best] = false;
            return true;
        }
    }
}
