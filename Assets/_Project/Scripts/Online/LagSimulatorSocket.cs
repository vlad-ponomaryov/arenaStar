using System;
using Arena.Net;
using Arena.Net.Transport;

namespace Arena.Online
{
    /// <summary>
    /// Dev network simulator (Server-Spec §10.1, §12.3; Development builds only): wraps the client socket and delays every
    /// datagram in both directions by <c>latency + random(0…jitter)</c> ms, dropping <c>loss</c> % of them. Datagrams wait in
    /// preallocated slots (a full queue drops — like a router); no allocations after construction. Polled from the main thread
    /// by the transport (<see cref="Send"/> / <see cref="TryReceive"/>).
    /// </summary>
    public sealed class LagSimulatorSocket : IDatagramSocket
    {
        private const long MicrosPerMs = 1000L;
        private const float PercentScale = 100f;

        private readonly IDatagramSocket _inner;
        private readonly Func<long> _clock;
        private readonly Random _random;
        private readonly long _latencyMicros;
        private readonly int _jitterMs;
        private readonly float _loss;
        private readonly DelayQueue _outgoing;
        private readonly DelayQueue _incoming;
        private readonly byte[] _receive = new byte[NetLimits.ReceiveBufferBytes];

        public LagSimulatorSocket(IDatagramSocket inner, int latencyMs, int jitterMs, float lossPercent, int slots, int seed,
            Func<long> clock = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (latencyMs < 0 || jitterMs < 0 || lossPercent < 0f || lossPercent > PercentScale || slots < 1)
                throw new ArgumentOutOfRangeException(nameof(latencyMs), "LagSimulatorSocket: bad conditions");
            _latencyMicros = latencyMs * MicrosPerMs;
            _jitterMs = jitterMs;
            _loss = lossPercent / PercentScale;
            _clock = clock ?? NetClock.NowMicros;
            _random = new Random(seed);
            _outgoing = new DelayQueue(slots, NetLimits.MaxPacketBytes);
            _incoming = new DelayQueue(slots, NetLimits.ReceiveBufferBytes);
        }

        public bool PathBroken => _inner.PathBroken;

        public long Dropped { get; private set; }

        public bool Send(byte[] buffer, int offset, int length)
        {
            long now = _clock();
            Flush(now);
            if (_random.NextDouble() < _loss)
            {
                Dropped++;
                return true; // lost on the way: the sender cannot know
            }
            if (!_outgoing.Push(buffer, offset, length, now + Delay())) Dropped++;
            Flush(now);
            return true;
        }

        public bool TryReceive(byte[] buffer, out int length)
        {
            long now = _clock();
            Flush(now);
            while (_inner.TryReceive(_receive, out int n))
            {
                if (_random.NextDouble() < _loss)
                {
                    Dropped++;
                    continue;
                }
                if (!_incoming.Push(_receive, 0, n, now + Delay())) Dropped++;
            }
            return _incoming.PopDue(now, buffer, out length);
        }

        public void Close() => _inner.Close();

        private long Delay() => _latencyMicros + (_jitterMs > 0 ? _random.Next(0, _jitterMs + 1) * MicrosPerMs : 0L);

        private void Flush(long now)
        {
            while (_outgoing.PopDue(now, _receive, out int length)) _inner.Send(_receive, 0, length);
        }
    }
}
