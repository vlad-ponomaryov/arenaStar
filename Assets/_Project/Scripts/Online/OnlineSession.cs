using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Arena.Bots;
using Arena.Core.Modes;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Settings;
using Arena.Net.Client;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arena.Online
{
    /// <summary>
    /// The online connection across scenes (Server-Spec §12.3, S3.10b; <c>DontDestroyOnLoad</c>, created in the Boot scene by
    /// SceneBuilder). Loads the content pack of <see cref="NetConfig"/> (the same file as the server, D-083), finds the server
    /// (the "Local (LAN)" region — <c>Discover</c> broadcast, §10.1; otherwise the region host, resolved in the background),
    /// connects to the lobby and queues with the chosen fighter (<see cref="LobbyClient"/>); the <see cref="MatchClientSession"/>
    /// is attached before the search, so a <c>MatchStart</c> in the same packet as <c>MatchAssigned</c> is not lost (S3.8).
    /// On <c>MatchStart</c> it loads the Match scene with an online <see cref="MatchLaunchParams"/>; the scene's
    /// <see cref="OnlineMatchBootstrap"/> takes the session (<see cref="TakeMatch"/>). Leaving the Match scene leaves the match
    /// and closes the connection (the next search connects again). Polls the network first in every frame
    /// (<c>DefaultExecutionOrder</c> before the HUD and the runner: snapshots of this frame count for this frame's ticks).
    /// Dev tools of Development builds: the delay/loss simulator on the socket (<see cref="LagSimulatorSocket"/>).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class OnlineSession : MonoBehaviour
    {
        /// <summary>Datagram slots per direction of the dev simulator (capacity, D-004).</summary>
        public const int DevSimSlots = 256;

        [SerializeField] private GameDatabase _database;

        private NetConfig _config;
        private ContentBundle _bundle;
        private ulong _contentHash;
        private TransportSettings _transport;
        private ClientMatchSettings _matchSettings;
        private LobbyClientSettings _lobbySettings;
        private readonly INetRandom _random = new CryptoNetRandom();
        private RejoinStore _rejoin;

        private NetClient _net;
        private LobbyClient _lobby;
        private MatchClientSession _match;
        private IDatagramSocket _socket;
        private UdpProbeSocket _probe;
        private LanDiscovery _discovery;
        private Task<IPAddress[]> _dns;
        private int _regionPort;

        private ModeKind _mode;
        private int _fighterIndex;
        private string _fighterId;
        private BotDifficulty _bots;
        private bool _rejoinSaved;

        /// <summary>The session of the running game, or null (no Boot scene: the Match scene opened directly, offline).</summary>
        public static OnlineSession Instance { get; private set; }

        /// <summary>Config and content pack are loaded: online play is possible.</summary>
        public bool IsAvailable { get; private set; }

        public OnlinePhase Phase { get; private set; }

        public OnlineFailure Failure { get; private set; }

        public NetConfig Config => _config;
        public ContentBundle Bundle => _bundle;
        public ulong ContentHash => _contentHash;
        public NetClient Net => _net;
        public LobbyClient Lobby => _lobby;

        /// <summary>The match session of the current search or match, or null.</summary>
        public MatchClientSession Match => _match;

        /// <summary>The socket is wrapped by the dev simulator.</summary>
        public bool Simulating { get; private set; }

        /// <summary>Searching, connecting or locating (the menu shows "Searching…" and offers "Cancel").</summary>
        public bool IsBusy => Phase == OnlinePhase.Locating || Phase == OnlinePhase.Connecting || Phase == OnlinePhase.Searching
            || Phase == OnlinePhase.Starting;

        /// <summary>Sets the database before <c>Awake</c> runs the init (a session created from code — tests).</summary>
        public void SetDatabase(GameDatabase database) => _database = database;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            if (_database != null) Init(_database);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Close();
            Instance = null;
        }

        private void OnApplicationQuit() => Close();

        /// <summary>Loads the config and the content pack. Without them online play stays off (offline still works).</summary>
        public void Init(GameDatabase database)
        {
            _database = database;
            IsAvailable = false;
            _config = database != null ? database.Net : null;
            if (_config == null || _config.ContentPack == null)
            {
                UnityNetLog.Warn("no NetConfig or content pack: online play is off");
                return;
            }
            try
            {
                _bundle = ContentPackReader.Read(_config.ContentPack.bytes, true, out ContentPackHeader header);
                _contentHash = header.ContentHash;
                _matchSettings = NetConfigConverter.ToClientMatchSettings(_config);
                _lobbySettings = NetConfigConverter.ToLobbySettings(_config);
            }
            catch (Exception e)
            {
                UnityNetLog.Warn("content pack or settings rejected: " + e.Message);
                return;
            }
            _transport = TransportSettings.CreateDefault();
            _rejoin = new RejoinStore(new PlayerPrefsKeyValueStore(), _config.ReconnectWindowSeconds);
            IsAvailable = true;
            UnityNetLog.Info("content " + ContentPackHeader.FormatHash(_contentHash) + ", protocol " + NetProtocol.Version);
        }

        /// <summary>Index of the fighter <paramref name="fighterId"/> in the content pack (the order of GameDatabase.Fighters), −1 if none.</summary>
        public int FighterIndexOf(string fighterId)
        {
            if (_bundle == null || fighterId == null) return -1;
            for (int i = 0; i < _bundle.Fighters.Length; i++)
                if (string.Equals(_bundle.Fighters[i].Id, fighterId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Starts a search of <paramref name="mode"/> in the default (first) region of the config.</summary>
        public void Search(ModeKind mode, string fighterId, BotDifficulty menuBots)
        {
            if (!BeginSearch(mode, fighterId, menuBots)) return;
            if (_config.Regions == null || _config.Regions.Count == 0 || _config.Regions[0] == null)
            {
                Fail(OnlineFailure.ServerUnreachable, "no regions in NetConfig");
                return;
            }
            NetRegionConfig region = _config.Regions[0];
            _regionPort = region.Port;
            Phase = OnlinePhase.Locating;
            if (region.IsLan)
            {
                try
                {
                    _probe = new UdpProbeSocket();
                    _discovery = new LanDiscovery(_lobbySettings, _contentHash, _random);
                    _discovery.Start(_probe, (ushort)region.Port, NetClock.NowMicros());
                    UnityNetLog.Info("looking for a LAN server on port " + region.Port);
                }
                catch (SocketException e)
                {
                    Fail(OnlineFailure.ServerUnreachable, "LAN discovery socket: " + e.SocketErrorCode);
                }
                return;
            }
            if (IPAddress.TryParse(region.Host, out IPAddress address))
            {
                ConnectTo(new IPEndPoint(address, region.Port));
                return;
            }
            _dns = Dns.GetHostAddressesAsync(region.Host); // in the background (§3.2)
            UnityNetLog.Info("resolving " + region.Host);
        }

        /// <summary>Starts a search on a known server (tests, a typed LAN address).</summary>
        public void SearchAt(IPEndPoint server, ModeKind mode, string fighterId, BotDifficulty menuBots)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (!BeginSearch(mode, fighterId, menuBots)) return;
            ConnectTo(server);
        }

        /// <summary>Stops the search and closes the connection.</summary>
        public void Cancel()
        {
            if (Phase == OnlinePhase.InMatch) return;
            if (_lobby != null) _lobby.Cancel();
            Close();
            Phase = OnlinePhase.Idle;
            UnityNetLog.Info("search cancelled");
        }

        /// <summary>Called by the Match scene's bootstrap: hands over the session whose <c>MatchStart</c> loaded the scene.</summary>
        public MatchClientSession TakeMatch()
        {
            if (Phase != OnlinePhase.Starting || _match == null || !_match.HasStart) return null;
            Phase = OnlinePhase.InMatch;
            return _match;
        }

        private bool BeginSearch(ModeKind mode, string fighterId, BotDifficulty menuBots)
        {
            if (!IsAvailable)
            {
                Fail(OnlineFailure.ServerUnreachable, "online play is off");
                return false;
            }
            if (Phase == OnlinePhase.InMatch) return false;
            Close();
            int index = FighterIndexOf(fighterId);
            if (index < 0)
            {
                Fail(OnlineFailure.Rejected, "unknown fighter " + fighterId);
                return false;
            }
            _mode = mode;
            _fighterId = fighterId;
            _fighterIndex = index;
            _bots = menuBots;
            Failure = OnlineFailure.None;
            return true;
        }

        private void ConnectTo(IPEndPoint server)
        {
            CloseLocator();
            try
            {
                IDatagramSocket socket = new UdpDatagramSocket(server);
                Simulating = Debug.isDebugBuild && _config.DevSimEnabled;
                if (Simulating)
                    socket = new LagSimulatorSocket(socket, _config.DevSimLatencyMs, _config.DevSimJitterMs, _config.DevSimLossPercent,
                        DevSimSlots, Environment.TickCount);
                _socket = socket;
            }
            catch (SocketException e)
            {
                Fail(OnlineFailure.ServerUnreachable, "socket to " + server + ": " + e.SocketErrorCode);
                return;
            }
            _net = new NetClient(_transport, _contentHash, _random);
            _lobby = new LobbyClient(_net);
            _match = new MatchClientSession(_net, _bundle, _matchSettings);
            _rejoinSaved = false;
            Phase = OnlinePhase.Connecting;
            _lobby.Search(_socket, _mode, _fighterIndex, NetClock.NowMicros());
            UnityNetLog.Info("connecting to " + server + (Simulating ? " (dev simulator on)" : ""));
        }

        private void Update()
        {
            long now = NetClock.NowMicros();
            if (Phase == OnlinePhase.Locating) UpdateLocating(now);
            if (_net == null) return;

            _net.Update(now);
            _lobby.Update(now);
            switch (Phase)
            {
                case OnlinePhase.Connecting:
                case OnlinePhase.Searching:
                    UpdateSearch();
                    break;
                case OnlinePhase.Starting:
                case OnlinePhase.InMatch:
                    UpdateMatch();
                    break;
            }
        }

        private void UpdateLocating(long now)
        {
            if (_discovery != null)
            {
                _discovery.Update(now);
                int found = _discovery.FirstJoinable();
                if (found >= 0)
                {
                    NetAddress address = _discovery.Get(found).Address;
                    ConnectTo(address.ToEndPoint(AddressFamily.InterNetwork));
                    return;
                }
                if (_discovery.IsRunning) return;
                bool incompatible = _discovery.Count > 0;
                Fail(incompatible ? OnlineFailure.UpdateRequired : OnlineFailure.ServerUnreachable,
                    incompatible ? "LAN servers found, none compatible" : "no LAN server answered");
                return;
            }
            if (_dns == null || !_dns.IsCompleted) return;
            Task<IPAddress[]> dns = _dns;
            _dns = null;
            if (dns.IsFaulted || dns.Result == null || dns.Result.Length == 0)
            {
                Fail(OnlineFailure.ServerUnreachable, "host did not resolve");
                return;
            }
            ConnectTo(new IPEndPoint(dns.Result[0], _regionPort));
        }

        private void UpdateSearch()
        {
            switch (_lobby.State)
            {
                case LobbyState.Searching:
                    if (Phase != OnlinePhase.Searching) UnityNetLog.Info("in the queue: " + _mode);
                    Phase = OnlinePhase.Searching;
                    break;
                case LobbyState.Rejected:
                    Fail(_lobby.QueueRejectReason == QueueRejectReason.Maintenance ? OnlineFailure.Maintenance : OnlineFailure.Rejected,
                        "queue rejected: " + _lobby.QueueRejectReason);
                    return;
                case LobbyState.Failed:
                    Fail(FailureOf(_lobby.ConnectRejectReason), "connection failed: " + _lobby.ConnectRejectReason + " / " +
                        _lobby.DisconnectReason);
                    return;
                case LobbyState.Assigned:
                    SaveRejoin();
                    break;
            }
            if (!_match.HasStart) return;
            SaveRejoin();
            Phase = OnlinePhase.Starting;
            UnityNetLog.Info("match start: slot " + _match.OwnSlot + ", map " + _match.Start.MapIndex + ", " + _match.Start.Mode);
            SceneFlow.PlayMatch(new MatchLaunchParams(_fighterId, _bots, 0, true));
        }

        private void UpdateMatch()
        {
            if (_match.HasEnded && _rejoinSaved)
            {
                _rejoin.Clear();
                _rejoinSaved = false;
                UnityNetLog.Info("match ended");
            }
        }

        private void SaveRejoin()
        {
            if (_rejoinSaved || _lobby.State != LobbyState.Assigned) return;
            MatchAssignedMessage a = _lobby.Assignment;
            var ticket = new RejoinTicket
            {
                Server = a.Address, MatchId = a.MatchId, Slot = a.Slot, Token = a.RejoinToken, SavedAtSeconds = RejoinStore.UnixNowSeconds()
            };
            _rejoin.Save(in ticket);
            _rejoinSaved = true;
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            if (Phase != OnlinePhase.InMatch && Phase != OnlinePhase.Starting) return;
            if (to.name == SceneNames.Match) return;
            if (_match != null && !_match.HasEnded) _match.Leave();
            if (_net != null) _net.Update(NetClock.NowMicros()); // flush the Leave
            if (_rejoin != null) _rejoin.Clear();
            _rejoinSaved = false;
            Close();
            Phase = OnlinePhase.Idle;
            UnityNetLog.Info("left the match");
        }

        private static OnlineFailure FailureOf(RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.VersionMismatch:
                case RejectReason.ContentMismatch:
                    return OnlineFailure.UpdateRequired;
                case RejectReason.Maintenance:
                    return OnlineFailure.Maintenance;
                case RejectReason.None:
                    return OnlineFailure.ServerUnreachable;
                default:
                    return OnlineFailure.Rejected;
            }
        }

        private void Fail(OnlineFailure failure, string message)
        {
            Close();
            Failure = failure;
            Phase = OnlinePhase.Failed;
            UnityNetLog.Warn(message);
        }

        private void CloseLocator()
        {
            _discovery = null;
            _dns = null;
            if (_probe != null) _probe.Close();
            _probe = null;
        }

        private void Close()
        {
            CloseLocator();
            if (_net != null) _net.Disconnect();
            if (_socket != null) _socket.Close();
            _net = null;
            _lobby = null;
            _match = null;
            _socket = null;
            Simulating = false;
        }
    }
}
