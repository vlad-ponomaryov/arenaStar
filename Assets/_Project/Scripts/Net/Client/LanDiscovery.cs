using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;

namespace Arena.Net.Client
{
    /// <summary>
    /// "Local (LAN)" region (Server-Spec §3.2, §6.3): broadcasts a connectionless <c>Discover</c> (padded to the reply size)
    /// to 255.255.255.255 on the game port <c>DiscoverRepeats</c> times over an unconnected menu socket
    /// (<see cref="UdpProbeSocket"/>, broadcast enabled; in tests an <see cref="InMemorySocket"/>) and collects the
    /// <c>DiscoverReply</c>s with its nonce, one entry per server address, until <c>DiscoverTimeoutMs</c> after the last
    /// broadcast. Servers answer private addresses only and only with <c>LanDiscovery</c> on (off on a VPS). Incompatible
    /// servers (other protocol or content) are listed with <see cref="LanServerInfo.Compatible"/> false. No allocations after
    /// construction.
    /// </summary>
    public sealed class LanDiscovery
    {
        private const long MicrosPerMs = 1000L;

        private readonly LobbyClientSettings _settings;
        private readonly INetRandom _random;
        private readonly ulong _contentHash;
        private readonly LanServerInfo[] _servers = new LanServerInfo[NetLimits.MaxLanServers];
        private readonly byte[] _buffer = new byte[NetLimits.ReceiveBufferBytes];
        private readonly BitWriter _writer = new BitWriter();
        private readonly BitReader _reader = new BitReader();
        private IServerDatagramSocket _socket;
        private NetAddress _target;
        private ulong _nonce;
        private int _sent;
        private long _startedAt;
        private long _lastSentAt;

        public LanDiscovery(LobbyClientSettings settings, ulong contentHash, INetRandom random)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _contentHash = contentHash;
        }

        public bool IsRunning { get; private set; }

        public int Count { get; private set; }

        /// <summary>Answers beyond <see cref="NetLimits.MaxLanServers"/> servers.</summary>
        public long DroppedServers { get; private set; }

        public long StrayPackets { get; private set; }

        public ref readonly LanServerInfo Get(int index) => ref _servers[index];

        /// <summary>Index of the first compatible server without maintenance, −1 if none.</summary>
        public int FirstJoinable()
        {
            for (int i = 0; i < Count; i++)
                if (_servers[i].Compatible && !_servers[i].Maintenance) return i;
            return -1;
        }

        /// <summary>Starts a search on <paramref name="port"/>; <paramref name="target"/> overrides the broadcast address (a known LAN IP).</summary>
        public void Start(IServerDatagramSocket socket, ushort port, long nowMicros, NetAddress target = default)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _target = target.IsEmpty ? NetAddress.IPv4Broadcast(port) : target;
            do _nonce = _random.NextULong();
            while (_nonce == 0ul);
            Count = 0;
            _sent = 0;
            _startedAt = nowMicros;
            IsRunning = true;
            SendDue(nowMicros);
        }

        public void Update(long nowMicros)
        {
            if (!IsRunning) return;
            while (_socket.TryReceive(_buffer, out int length, out NetAddress from)) OnPacket(length, from, nowMicros);
            SendDue(nowMicros);
            if (_sent >= _settings.DiscoverRepeats && nowMicros - _lastSentAt >= _settings.DiscoverTimeoutMs * MicrosPerMs)
                IsRunning = false;
        }

        private void SendDue(long now)
        {
            while (_sent < _settings.DiscoverRepeats && now >= _startedAt + _sent * _settings.DiscoverIntervalMs * MicrosPerMs)
            {
                _sent++;
                var discover = new DiscoverMessage { ProtocolId = NetProtocol.Id, Nonce = _nonce };
                int body = NetProtocol.DiscoverPacketBytes - NetProtocol.HeaderBytes;
                _writer.Reset(_buffer, NetProtocol.HeaderBytes, body);
                discover.Write(_writer);
                _writer.WriteZeroBytes(body - _writer.BytesWritten);
                PacketHeader.Connectionless(PacketKind.Discover).Write(_buffer);
                _socket.SendTo(_buffer, 0, NetProtocol.DiscoverPacketBytes, _target);
                _lastSentAt = now;
            }
        }

        private void OnPacket(int length, in NetAddress from, long now)
        {
            if (length != NetProtocol.DiscoverPacketBytes || !PacketHeader.TryRead(_buffer, length, out PacketHeader header)
                || header.Kind != PacketKind.DiscoverReply)
            {
                StrayPackets++;
                return;
            }
            _reader.Reset(_buffer, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            if (!DiscoverReplyMessage.Read(_reader, out DiscoverReplyMessage reply) || reply.ProtocolId != NetProtocol.Id
                || reply.Nonce != _nonce)
            {
                StrayPackets++;
                return;
            }
            int index = -1;
            for (int i = 0; i < Count && index < 0; i++)
                if (_servers[i].Address == from) index = i;
            if (index < 0)
            {
                if (Count == _servers.Length)
                {
                    DroppedServers++;
                    return;
                }
                index = Count++;
                _servers[index].ReplyMicros = now - _startedAt;
            }
            ref LanServerInfo s = ref _servers[index];
            s.Address = from;
            s.ProtocolVersion = reply.ProtocolVersion;
            s.ContentHash = reply.ContentHash;
            s.Compatible = reply.ProtocolVersion == NetProtocol.Version && reply.ContentHash == _contentHash;
            s.ServerNameKey = reply.ServerNameKey;
            s.Connections = reply.Connections;
            s.Capacity = reply.Capacity;
            s.Maintenance = reply.Maintenance;
        }
    }
}
