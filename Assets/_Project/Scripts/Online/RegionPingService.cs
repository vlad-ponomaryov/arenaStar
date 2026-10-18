using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Arena.Data;
using Arena.Net;
using Arena.Net.Client;
using Arena.Net.Transport;

namespace Arena.Online
{
    /// <summary>
    /// Ping of the menu regions (Server-Spec §6.3, S3.11): first every region gets an address — the "Local (LAN)" region by a
    /// <c>Discover</c> broadcast (<see cref="LanDiscovery"/>, the first joinable server), a region with an IP at once, a host name
    /// in the background (<c>Dns.GetHostAddressesAsync</c>, never in a frame — §3.2); then the <see cref="RegionPinger"/> measures
    /// all found addresses in parallel on the same unconnected socket. The result per region is the median in ms (−1 — no answer)
    /// and the badge quality; <see cref="BestRegion"/> — the lowest median. One run at a time; the socket comes from a factory
    /// (a <see cref="UdpProbeSocket"/> in the game, an in-memory socket in tests) and is closed at the end.
    /// </summary>
    public sealed class RegionPingService
    {
        private enum Stage : byte
        {
            Idle = 0,
            Resolving = 1,
            Pinging = 2,
        }

        private readonly LobbyClientSettings _settings;
        private readonly Func<IServerDatagramSocket> _socketFactory;
        private readonly RegionPinger _pinger;
        private readonly LanDiscovery _discovery;
        private readonly NetRegionConfig[] _regions;
        private readonly int[] _pingMs;
        private readonly bool[] _resolved;
        private readonly bool[] _failed;
        private readonly NetAddress[] _addresses;
        private readonly Task<IPAddress[]>[] _dns;
        private readonly NetAddress[] _pingTargets = new NetAddress[NetLimits.MaxRegions];
        private readonly int[] _pingRegion = new int[NetLimits.MaxRegions];
        private IServerDatagramSocket _socket;
        private Stage _stage;
        private int _lanRegion = -1;
        private int _pingCount;

        /// <param name="regions">Regions of <c>NetConfig</c> in menu order (at most <see cref="NetLimits.MaxRegions"/> are used).</param>
        /// <param name="socketFactory">Opens the unconnected probe socket of one run (broadcast allowed).</param>
        public RegionPingService(IReadOnlyList<NetRegionConfig> regions, LobbyClientSettings settings, ulong contentHash,
            INetRandom random, Func<IServerDatagramSocket> socketFactory)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _pinger = new RegionPinger(settings, random);
            _discovery = new LanDiscovery(settings, contentHash, random);
            int count = regions.Count < NetLimits.MaxRegions ? regions.Count : NetLimits.MaxRegions;
            _regions = new NetRegionConfig[count];
            for (int i = 0; i < count; i++) _regions[i] = regions[i];
            _pingMs = new int[count];
            _resolved = new bool[count];
            _failed = new bool[count];
            _addresses = new NetAddress[count];
            _dns = new Task<IPAddress[]>[count];
            for (int i = 0; i < count; i++) _pingMs[i] = -1;
        }

        public int Count => _regions.Length;

        public bool IsRunning => _stage != Stage.Idle;

        /// <summary>Runs finished (tests, the menu refreshes its labels on a change).</summary>
        public int Runs { get; private set; }

        public NetRegionConfig Region(int region) => _regions[region];

        /// <summary>Median round trip of the last run, ms; −1 — not measured or no answer.</summary>
        public int PingMs(int region) => _pingMs[region];

        public PingQuality Quality(int region) => RegionPinger.Quality(_pingMs[region], _settings);

        /// <summary>The region with the lowest ping of the last run, −1 if none answered.</summary>
        public int BestRegion()
        {
            int best = -1;
            for (int i = 0; i < _pingMs.Length; i++)
                if (_pingMs[i] >= 0 && (best < 0 || _pingMs[i] < _pingMs[best])) best = i;
            return best;
        }

        /// <summary>Starts a run (ignored while one runs). False — the probe socket could not be opened.</summary>
        public bool Start(long nowMicros)
        {
            if (_stage != Stage.Idle) return true;
            if (_regions.Length == 0) return false;
            try
            {
                _socket = _socketFactory();
            }
            catch (SocketException e)
            {
                UnityNetLog.Warn("region ping socket: " + e.SocketErrorCode);
                return false;
            }
            _lanRegion = -1;
            for (int i = 0; i < _regions.Length; i++)
            {
                _resolved[i] = false;
                _failed[i] = false;
                _dns[i] = null;
                NetRegionConfig r = _regions[i];
                if (r == null)
                {
                    _failed[i] = true;
                    continue;
                }
                if (r.IsLan)
                {
                    if (_lanRegion >= 0) _failed[i] = true; // one LAN region is enough
                    else _lanRegion = i;
                    continue;
                }
                if (IPAddress.TryParse(r.Host, out IPAddress ip))
                {
                    _addresses[i] = NetAddress.FromEndPoint(new IPEndPoint(ip, r.Port));
                    _resolved[i] = true;
                    continue;
                }
                try
                {
                    _dns[i] = Dns.GetHostAddressesAsync(r.Host);
                }
                catch (ArgumentException)
                {
                    _failed[i] = true;
                }
            }
            if (_lanRegion >= 0) _discovery.Start(_socket, (ushort)_regions[_lanRegion].Port, nowMicros);
            _stage = Stage.Resolving;
            Update(nowMicros);
            return true;
        }

        public void Update(long nowMicros)
        {
            if (_stage == Stage.Resolving) UpdateResolving(nowMicros);
            if (_stage != Stage.Pinging) return;
            _pinger.Update(nowMicros);
            if (_pinger.IsRunning) return;
            for (int p = 0; p < _pingCount; p++) _pingMs[_pingRegion[p]] = _pinger.MedianMs(p);
            Finish();
        }

        /// <summary>Stops a run and closes its socket (results of the last finished run stay).</summary>
        public void Stop()
        {
            if (_stage == Stage.Idle) return;
            _stage = Stage.Idle;
            CloseSocket();
        }

        private void UpdateResolving(long now)
        {
            if (_lanRegion >= 0 && !_resolved[_lanRegion] && !_failed[_lanRegion])
            {
                _discovery.Update(now);
                int found = _discovery.FirstJoinable();
                if (found >= 0)
                {
                    _addresses[_lanRegion] = _discovery.Get(found).Address;
                    _resolved[_lanRegion] = true;
                }
                else if (!_discovery.IsRunning) _failed[_lanRegion] = true;
            }
            for (int i = 0; i < _regions.Length; i++)
            {
                Task<IPAddress[]> dns = _dns[i];
                if (dns == null || !dns.IsCompleted) continue;
                _dns[i] = null;
                IPAddress ip = dns.IsFaulted || dns.IsCanceled ? null : FirstIPv4(dns.Result);
                if (ip == null)
                {
                    _failed[i] = true;
                    continue;
                }
                _addresses[i] = NetAddress.FromEndPoint(new IPEndPoint(ip, _regions[i].Port));
                _resolved[i] = true;
            }
            _pingCount = 0;
            for (int i = 0; i < _regions.Length; i++)
            {
                if (!_resolved[i] && !_failed[i]) return; // still resolving
                if (!_resolved[i]) continue;
                _pingTargets[_pingCount] = _addresses[i];
                _pingRegion[_pingCount] = i;
                _pingCount++;
            }
            for (int i = 0; i < _pingMs.Length; i++) _pingMs[i] = -1;
            if (_pingCount == 0)
            {
                Finish();
                return;
            }
            _pinger.Start(_socket, _pingTargets, _pingCount, now);
            _stage = Stage.Pinging;
        }

        private static IPAddress FirstIPv4(IPAddress[] list)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Length; i++)
                if (list[i].AddressFamily == AddressFamily.InterNetwork) return list[i];
            return list.Length > 0 ? list[0] : null;
        }

        private void Finish()
        {
            _stage = Stage.Idle;
            Runs++;
            CloseSocket();
        }

        private void CloseSocket()
        {
            if (_socket != null) _socket.Close();
            _socket = null;
        }
    }
}
