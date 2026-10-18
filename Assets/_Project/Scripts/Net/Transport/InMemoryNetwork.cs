using System;
using Arena.Core.Random;

namespace Arena.Net.Transport
{
    /// <summary>
    /// A virtual datagram network on a virtual clock for deterministic tests (Server-Spec §3.2, §11.1). Endpoints are
    /// <see cref="InMemorySocket"/>s bound to <see cref="NetAddress"/>es; each endpoint's outgoing link applies its
    /// <see cref="NetworkConditions"/> (latency, jitter, loss with bursts, duplicates, reordering, bandwidth). The RNG is
    /// seeded, so a failing test repeats exactly. Packet storage is preallocated; sending and receiving do not allocate.
    /// Single-threaded.
    /// </summary>
    public sealed class InMemoryNetwork
    {
        private const float MicrosPerMs = 1000f;
        private const float PercentScale = 100f;
        private const long MicrosPerSecond = 1000000L;

        private readonly byte[][] _data;
        private readonly int[] _length;
        private readonly long[] _deliverAt;
        private readonly NetAddress[] _from;
        private readonly NetAddress[] _to;
        private readonly int[] _free;
        private int _freeCount;

        // Pending packets, sorted by delivery time (stable: equal times keep the send order).
        private readonly int[] _pending;
        private int _pendingCount;

        private InMemorySocket[] _endpoints = new InMemorySocket[8];
        private int _endpointCount;
        private DeterministicRng _rng;

        public InMemoryNetwork(int seed, int capacityPackets = 4096)
        {
            if (capacityPackets <= 0) throw new ArgumentOutOfRangeException(nameof(capacityPackets));
            _rng = DeterministicRng.FromSeed(seed);
            _data = new byte[capacityPackets][];
            _length = new int[capacityPackets];
            _deliverAt = new long[capacityPackets];
            _from = new NetAddress[capacityPackets];
            _to = new NetAddress[capacityPackets];
            _free = new int[capacityPackets];
            _pending = new int[capacityPackets];
            for (int i = 0; i < capacityPackets; i++)
            {
                _data[i] = new byte[NetLimits.ReceiveBufferBytes];
                _free[i] = capacityPackets - 1 - i;
            }
            _freeCount = capacityPackets;
            DefaultConditions = new NetworkConditions();
        }

        /// <summary>Virtual time, microseconds.</summary>
        public long NowMicros { get; private set; }

        /// <summary>Conditions of endpoints without their own (a perfect link unless changed).</summary>
        public NetworkConditions DefaultConditions { get; set; }

        /// <summary>
        /// Test hook: called for every sent datagram (from, to, buffer, offset, length); false drops it. Null — no filter.
        /// </summary>
        public Func<NetAddress, NetAddress, byte[], int, int, bool> SendFilter { get; set; }

        public long SentCount { get; private set; }
        public long LostCount { get; private set; }
        public long DuplicatedCount { get; private set; }
        public long DeliveredCount { get; private set; }
        public long OverflowCount { get; private set; }
        public long SentBytes { get; private set; }

        public int PendingCount => _pendingCount;

        public void SetTime(long nowMicros)
        {
            if (nowMicros < NowMicros) throw new ArgumentOutOfRangeException(nameof(nowMicros), "time goes forward only");
            NowMicros = nowMicros;
            DropUndeliverable();
        }

        public void Advance(long micros) => SetTime(NowMicros + micros);

        /// <summary>An unconnected endpoint (the server side). Allocates.</summary>
        public InMemorySocket CreateSocket(NetAddress local)
        {
            if (FindEndpoint(local) != null) throw new InvalidOperationException("address in use: " + local);
            var socket = new InMemorySocket(this, local);
            if (_endpointCount == _endpoints.Length) Array.Resize(ref _endpoints, _endpoints.Length * 2);
            _endpoints[_endpointCount++] = socket;
            return socket;
        }

        /// <summary>A connected endpoint (the client side): receives only from <paramref name="remote"/>. Allocates.</summary>
        public InMemoryClientSocket CreateClientSocket(NetAddress local, NetAddress remote) =>
            new InMemoryClientSocket(CreateSocket(local), remote);

        internal void Remove(InMemorySocket socket)
        {
            for (int i = 0; i < _endpointCount; i++)
            {
                if (_endpoints[i] != socket) continue;
                _endpoints[i] = _endpoints[--_endpointCount];
                _endpoints[_endpointCount] = null;
                break;
            }
            DropUndeliverable();
        }

        internal bool Send(InMemorySocket from, in NetAddress to, byte[] buffer, int offset, int length)
        {
            if (length < 0 || length > NetLimits.ReceiveBufferBytes) return false;
            SentCount++;
            SentBytes += length;
            if (SendFilter != null && !SendFilter(from.Local, to, buffer, offset, length))
            {
                LostCount++;
                return true;
            }
            NetworkConditions c = from.Conditions ?? DefaultConditions;
            if (IsLost(from, c))
            {
                LostCount++;
                return true;
            }
            long delay = DelayMicros(c);
            if (c.BandwidthBytesPerSecond > 0)
            {
                long start = from.LinkBusyUntil > NowMicros ? from.LinkBusyUntil : NowMicros;
                if (start - NowMicros > (long)(c.MaxQueueMs * MicrosPerMs))
                {
                    LostCount++;
                    return true;
                }
                long serialization = length * MicrosPerSecond / c.BandwidthBytesPerSecond;
                from.LinkBusyUntil = start + serialization;
                delay += from.LinkBusyUntil - NowMicros;
            }
            Enqueue(from.Local, to, buffer, offset, length, NowMicros + delay);
            if (c.DuplicatePercent > 0f && Chance(c.DuplicatePercent))
            {
                DuplicatedCount++;
                Enqueue(from.Local, to, buffer, offset, length, NowMicros + delay + DelayMicros(c));
            }
            return true;
        }

        internal bool Receive(InMemorySocket socket, bool filterRemote, in NetAddress remote, byte[] buffer, out int length,
            out NetAddress from)
        {
            for (int p = 0; p < _pendingCount; p++)
            {
                int slot = _pending[p];
                if (_deliverAt[slot] > NowMicros) break;
                if (_to[slot] != socket.Local) continue;
                if (filterRemote && _from[slot] != remote)
                {
                    RemovePendingAt(p);
                    p--;
                    continue;
                }
                length = _length[slot];
                from = _from[slot];
                if (length > buffer.Length)
                {
                    RemovePendingAt(p);
                    p--;
                    socket.OversizeCount++;
                    continue;
                }
                Buffer.BlockCopy(_data[slot], 0, buffer, 0, length);
                RemovePendingAt(p);
                DeliveredCount++;
                return true;
            }
            length = 0;
            from = default;
            return false;
        }

        private bool IsLost(InMemorySocket from, NetworkConditions c)
        {
            if (c.BurstEnterPercent > 0f)
            {
                if (from.InBurst)
                {
                    if (Chance(c.BurstExitPercent)) from.InBurst = false;
                }
                else if (Chance(c.BurstEnterPercent))
                {
                    from.InBurst = true;
                }
                if (from.InBurst) return Chance(c.BurstLossPercent);
            }
            return c.LossPercent > 0f && Chance(c.LossPercent);
        }

        private long DelayMicros(NetworkConditions c)
        {
            float ms = c.LatencyMs;
            if (c.JitterMs > 0f) ms += _rng.NextFloat01() * c.JitterMs;
            if (c.ReorderPercent > 0f && Chance(c.ReorderPercent)) ms += c.ReorderExtraMs;
            return (long)(ms * MicrosPerMs);
        }

        private bool Chance(float percent) => _rng.NextFloat01() * PercentScale < percent;

        private void Enqueue(in NetAddress from, in NetAddress to, byte[] buffer, int offset, int length, long deliverAt)
        {
            if (_freeCount == 0)
            {
                OverflowCount++;
                return;
            }
            int slot = _free[--_freeCount];
            Buffer.BlockCopy(buffer, offset, _data[slot], 0, length);
            _length[slot] = length;
            _from[slot] = from;
            _to[slot] = to;
            _deliverAt[slot] = deliverAt;
            int p = _pendingCount;
            while (p > 0 && _deliverAt[_pending[p - 1]] > deliverAt)
            {
                _pending[p] = _pending[p - 1];
                p--;
            }
            _pending[p] = slot;
            _pendingCount++;
        }

        private void RemovePendingAt(int p)
        {
            int slot = _pending[p];
            for (int i = p + 1; i < _pendingCount; i++) _pending[i - 1] = _pending[i];
            _pendingCount--;
            _free[_freeCount++] = slot;
        }

        /// <summary>Due packets to an address nobody listens on are gone (like UDP to a closed port).</summary>
        private void DropUndeliverable()
        {
            for (int p = 0; p < _pendingCount; p++)
            {
                int slot = _pending[p];
                if (_deliverAt[slot] > NowMicros) break;
                if (FindEndpoint(_to[slot]) != null) continue;
                RemovePendingAt(p);
                p--;
            }
        }

        private InMemorySocket FindEndpoint(in NetAddress address)
        {
            for (int i = 0; i < _endpointCount; i++)
                if (_endpoints[i].Local == address) return _endpoints[i];
            return null;
        }
    }
}
