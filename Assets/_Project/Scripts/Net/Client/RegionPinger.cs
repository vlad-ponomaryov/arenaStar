using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;

namespace Arena.Net.Client
{
    /// <summary>
    /// Region ping before a search (Server-Spec §6.3): <c>PingSamples</c> connectionless <c>Ping</c>s to every region in parallel
    /// (one every <c>PingIntervalMs</c>), over an unconnected menu socket (<see cref="UdpProbeSocket"/>; in tests an
    /// <see cref="InMemorySocket"/>); the pong echoes the nonce, the round trip is measured here. The result per region is the
    /// median of the answered samples; the best region is the one with the lowest median. Region addresses are resolved before
    /// (DNS in the background, never in a frame — §3.2). No allocations after construction.
    /// </summary>
    public sealed class RegionPinger
    {
        private const long MicrosPerMs = 1000L;
        private const int SampleBits = 8;
        private const ulong IndexMask = (1ul << (2 * SampleBits)) - 1ul;

        private readonly LobbyClientSettings _settings;
        private readonly INetRandom _random;
        private readonly NetAddress[] _regions = new NetAddress[NetLimits.MaxRegions];
        private readonly long[,] _sentAt = new long[NetLimits.MaxRegions, NetLimits.MaxPingSamples];
        private readonly long[,] _rtt = new long[NetLimits.MaxRegions, NetLimits.MaxPingSamples];
        private readonly long[] _sorted = new long[NetLimits.MaxPingSamples];
        private readonly byte[] _buffer = new byte[NetLimits.ReceiveBufferBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();
        private IServerDatagramSocket _socket;
        private ulong _salt;
        private int _samplesSent;
        private long _startedAt;
        private long _lastSentAt;

        public RegionPinger(LobbyClientSettings settings, INetRandom random)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int RegionCount { get; private set; }

        public bool IsRunning { get; private set; }

        /// <summary>Every sample of every region answered or timed out.</summary>
        public bool IsDone => !IsRunning && RegionCount > 0;

        public long StrayPackets { get; private set; }

        public NetAddress RegionAt(int region) => _regions[region];

        /// <summary>Starts measuring <paramref name="count"/> regions; the socket is not owned.</summary>
        public void Start(IServerDatagramSocket socket, NetAddress[] regions, int count, long nowMicros)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (count <= 0 || count > NetLimits.MaxRegions || count > regions.Length)
                throw new ArgumentException("RegionPinger: 1 … MaxRegions regions");
            RegionCount = count;
            for (int r = 0; r < count; r++)
            {
                _regions[r] = regions[r];
                for (int s = 0; s < NetLimits.MaxPingSamples; s++)
                {
                    _sentAt[r, s] = -1L;
                    _rtt[r, s] = -1L;
                }
            }
            _salt = _random.NextULong() & ~IndexMask;
            _samplesSent = 0;
            _startedAt = nowMicros;
            IsRunning = true;
            SendDue(nowMicros);
        }

        public void Update(long nowMicros)
        {
            if (!IsRunning) return;
            while (_socket.TryReceive(_buffer, out int length, out NetAddress from)) OnPacket(length, from, nowMicros);
            SendDue(nowMicros);
            if (_samplesSent >= _settings.PingSamples
                && (AllAnswered() || nowMicros - _lastSentAt >= _settings.PingTimeoutMs * MicrosPerMs))
                IsRunning = false;
        }

        /// <summary>Answered samples of <paramref name="region"/>.</summary>
        public int Answered(int region)
        {
            int n = 0;
            for (int s = 0; s < _settings.PingSamples; s++)
                if (_rtt[region, s] >= 0L) n++;
            return n;
        }

        /// <summary>Median round trip of the answered samples, microseconds; −1 — no answer.</summary>
        public long MedianMicros(int region)
        {
            int n = 0;
            for (int s = 0; s < _settings.PingSamples; s++)
            {
                long v = _rtt[region, s];
                if (v < 0L) continue;
                int i = n++;
                while (i > 0 && _sorted[i - 1] > v)
                {
                    _sorted[i] = _sorted[i - 1];
                    i--;
                }
                _sorted[i] = v;
            }
            if (n == 0) return -1L;
            return (n & 1) == 1 ? _sorted[n / 2] : (_sorted[n / 2 - 1] + _sorted[n / 2]) / 2;
        }

        /// <summary>Median in whole milliseconds (rounded), −1 — no answer.</summary>
        public int MedianMs(int region)
        {
            long micros = MedianMicros(region);
            return micros < 0L ? -1 : (int)((micros + MicrosPerMs / 2) / MicrosPerMs);
        }

        /// <summary>The region with the lowest median, −1 if none answered.</summary>
        public int BestRegion()
        {
            int best = -1;
            long bestMicros = long.MaxValue;
            for (int r = 0; r < RegionCount; r++)
            {
                long m = MedianMicros(r);
                if (m < 0L || m >= bestMicros) continue;
                best = r;
                bestMicros = m;
            }
            return best;
        }

        public PingQuality QualityOf(int region) => Quality(MedianMs(region), _settings);

        /// <summary>Green below <c>PingGoodMs</c>, yellow below <c>PingFairMs</c>, red from it (GDD §8.2); −1 — unknown.</summary>
        public static PingQuality Quality(int pingMs, LobbyClientSettings settings)
        {
            if (pingMs < 0) return PingQuality.Unknown;
            if (pingMs < settings.PingGoodMs) return PingQuality.Good;
            return pingMs < settings.PingFairMs ? PingQuality.Fair : PingQuality.Poor;
        }

        private void SendDue(long now)
        {
            while (_samplesSent < _settings.PingSamples && now >= _startedAt + _samplesSent * _settings.PingIntervalMs * MicrosPerMs)
            {
                int sample = _samplesSent++;
                for (int r = 0; r < RegionCount; r++)
                {
                    var ping = new PingMessage { ProtocolId = NetProtocol.Id, Nonce = _salt | ((ulong)(uint)r << SampleBits) | (uint)sample };
                    _writer.Reset(_buffer, NetProtocol.HeaderBytes, NetProtocol.PingPacketBytes - NetProtocol.HeaderBytes);
                    ping.Write(_writer);
                    _writer.WriteZeroBytes(NetProtocol.PingPacketBytes - NetProtocol.HeaderBytes - _writer.BytesWritten);
                    PacketHeader.Connectionless(PacketKind.Ping).Write(_buffer);
                    _sentAt[r, sample] = now;
                    _socket.SendTo(_buffer, 0, NetProtocol.PingPacketBytes, _regions[r]);
                }
                _lastSentAt = now;
            }
        }

        private void OnPacket(int length, in NetAddress from, long now)
        {
            if (length != NetProtocol.PingPacketBytes || !PacketHeader.TryRead(_buffer, length, out PacketHeader header)
                || header.Kind != PacketKind.Pong)
            {
                StrayPackets++;
                return;
            }
            _reader.Reset(_buffer, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!PingMessage.Read(_reader, out PingMessage pong) || pong.ProtocolId != NetProtocol.Id
                || (pong.Nonce & ~IndexMask) != _salt)
            {
                StrayPackets++;
                return;
            }
            int region = (int)((pong.Nonce >> SampleBits) & ((1ul << SampleBits) - 1ul));
            int sample = (int)(pong.Nonce & ((1ul << SampleBits) - 1ul));
            if (region >= RegionCount || sample >= _settings.PingSamples || _regions[region] != from || _sentAt[region, sample] < 0L
                || _rtt[region, sample] >= 0L)
            {
                StrayPackets++;
                return;
            }
            _rtt[region, sample] = now - _sentAt[region, sample];
        }

        private bool AllAnswered()
        {
            for (int r = 0; r < RegionCount; r++)
                if (Answered(r) < _settings.PingSamples) return false;
            return true;
        }
    }
}
