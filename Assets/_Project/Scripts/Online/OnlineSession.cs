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
    /// S3.11: the region choice of the menu (pinged by <see cref="RegionPingService"/>; the best one is chosen until the player
    /// picks one), the search clock of the matchmaking screen and "Play again" after an online match (<see cref="SearchAgain"/>:
    /// leaves the ended match, closes its connection and queues again with the same mode and fighter on a new connection).
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
        private ReconnectSettings _reconnectSettings;
        private readonly INetRandom _random = new CryptoNetRandom();
        private RejoinStore _rejoin;
        private RejoinTicket _ticket;
        private bool _hasTicket;
        private bool _rejoining;
        private IPEndPoint _server;
        private Func<IDatagramSocket> _socketFactory;

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
        private bool _hasLastSearch;
        private bool _regionRemote;
        private long _searchStartedMicros;

        private RegionPingService _pings;
        private int _regionIndex;
        private bool _regionChosen;
        private int _pingRunsSeen;

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

        /// <summary>Region pings of the menu, or null (online play is off).</summary>
        public RegionPingService Pings => _pings;

        /// <summary>Number of regions in the config.</summary>
        public int RegionCount => _config != null && _config.Regions != null ? _config.Regions.Count : 0;

        /// <summary>The region the next search goes to (the best by ping until the player picks one).</summary>
        public int RegionIndex => _regionIndex;

        /// <summary>The region of <see cref="RegionIndex"/>, or null.</summary>
        public NetRegionConfig SelectedRegion => _regionIndex < RegionCount ? _config.Regions[_regionIndex] : null;

        /// <summary>Median ping of the selected region from the last menu ping, ms (-1 - unknown).</summary>
        public int SelectedPingMs => _pings != null && _regionIndex < _pings.Count ? _pings.PingMs(_regionIndex) : -1;

        public PingQuality SelectedPingQuality => _pings != null && _regionIndex < _pings.Count ? _pings.Quality(_regionIndex)
            : PingQuality.Unknown;

        /// <summary>Mode of the current or last search.</summary>
        public ModeKind SearchMode => _mode;

        /// <summary>A search was made in this run of the game (<see cref="SearchAgain"/> can repeat it).</summary>
        public bool HasLastSearch => _hasLastSearch;

        /// <summary>Seconds since the current search started (the matchmaking clock before the server sends a status).</summary>
        public int SearchSeconds => IsBusy ? (int)((NetClock.NowMicros() - _searchStartedMicros) / MicrosPerSecond) : 0;

        /// <summary>
        /// Every search goes to this server instead of the region (PlayMode tests: a server in the process on 127.0.0.1). Never
        /// set by the game: the addresses are fixed in the build (§6.3).
        /// </summary>
        public IPEndPoint ServerOverride { get; set; }

        /// <summary>
        /// A ticket of an unfinished match is saved (the app was closed or the attempts gave up, Server-Spec §7) and younger than the
        /// return window: the menu shows "Back to the match" (<see cref="Rejoin"/>).
        /// </summary>
        public bool CanRejoin => IsAvailable && _hasTicket && !IsBusy && Phase != OnlinePhase.InMatch
            && RejoinStore.UnixNowSeconds() - _ticket.SavedAtSeconds <= _config.ReconnectWindowSeconds;

        /// <summary>The current search is a return to a saved match ("Back to the match").</summary>
        public bool IsRejoining => _rejoining;

        private const long MicrosPerSecond = 1000000L;

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

        private void OnApplicationQuit()
        {
            RefreshTicket(); // quitting mid-match: "Back to the match" on the next start
            Close();
        }

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
                _reconnectSettings = NetConfigConverter.ToReconnectSettings(_config);
            }
            catch (Exception e)
            {
                UnityNetLog.Warn("content pack or settings rejected: " + e.Message);
                return;
            }
            _transport = TransportSettings.CreateDefault();
            _rejoin = new RejoinStore(new PlayerPrefsKeyValueStore(), _config.ReconnectWindowSeconds);
            _hasTicket = _rejoin.TryLoad(RejoinStore.UnixNowSeconds(), out _ticket);
            _socketFactory = CreateServerSocket;
            _pings = _config.Regions != null && _config.Regions.Count > 0
                ? new RegionPingService(_config.Regions, _lobbySettings, _contentHash, _random, () => new UdpProbeSocket())
                : null;
            _regionIndex = 0;
            _regionChosen = false;
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

        /// <summary>Pings the regions again (the menu calls it when shown; ignored while a ping runs or online is off).</summary>
        public void RefreshPings()
        {
            if (_pings != null) _pings.Start(NetClock.NowMicros());
        }

        /// <summary>The player picks the region of the next search (the menu's region button).</summary>
        public void SelectRegion(int index)
        {
            if (index < 0 || index >= RegionCount) return;
            _regionIndex = index;
            _regionChosen = true;
        }

        /// <summary>Starts a search of <paramref name="mode"/> in the selected region (<see cref="RegionIndex"/>).</summary>
        public void Search(ModeKind mode, string fighterId, BotDifficulty menuBots)
        {
            if (!BeginSearch(mode, fighterId, menuBots)) return;
            if (ServerOverride != null)
            {
                _regionRemote = false;
                ConnectTo(ServerOverride);
                return;
            }
            NetRegionConfig region = SelectedRegion;
            if (region == null)
            {
                Fail(OnlineFailure.ServerUnreachable, "no regions in NetConfig");
                return;
            }
            _regionPort = region.Port;
            _regionRemote = !region.IsLan;
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
            _regionRemote = false;
            ConnectTo(server);
        }

        /// <summary>
        /// "Play again" after an online match (§12.3): leaves the match (its connection closes; the server room keeps the result
        /// for its linger time) and queues again with the mode, fighter and region of the last search. False - no search was made.
        /// </summary>
        public bool SearchAgain()
        {
            if (!_hasLastSearch || !IsAvailable) return false;
            LeaveMatch();
            Search(_mode, _fighterId, _bots);
            return true;
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
            _hasLastSearch = true;
            _searchStartedMicros = NetClock.NowMicros();
            Failure = OnlineFailure.None;
            return true;
        }

        private void ConnectTo(IPEndPoint server)
        {
            if (!OpenConnection(server)) return;
            _rejoining = false;
            Phase = OnlinePhase.Connecting;
            _lobby.Search(_socket, _mode, _fighterIndex, NetClock.NowMicros());
            UnityNetLog.Info("connecting to " + server + (Simulating ? " (dev simulator on)" : ""));
        }

        /// <summary>
        /// "Back to the match" from the menu (Server-Spec §7): connects with <c>Rejoin</c> and the saved ticket to the saved server;
        /// <c>MatchStart</c> loads the Match scene as after a search, <c>MatchOver</c> forgets the ticket ("the match is already over").
        /// </summary>
        public void Rejoin()
        {
            if (!CanRejoin) return;
            Close();
            Failure = OnlineFailure.None;
            RejoinTicket ticket = _ticket;
            IPEndPoint server = ServerOverride ?? ticket.Server.ToEndPoint(ticket.Server.IsIPv4 ? AddressFamily.InterNetwork
                : AddressFamily.InterNetworkV6);
            if (!OpenConnection(server)) return;
            _rejoining = true;
            _rejoinSaved = true; // the ticket is already in the store
            _searchStartedMicros = NetClock.NowMicros();
            Phase = OnlinePhase.Connecting;
            _net.Connect(_socket, ConnectPurpose.Rejoin, ticket.Token, NetClock.NowMicros());
            UnityNetLog.Info("returning to match " + ticket.MatchId.ToString("x") + " at " + server);
        }

        /// <summary>Socket, client, lobby and match session to <paramref name="server"/>; false — the socket failed (reported).</summary>
        private bool OpenConnection(IPEndPoint server)
        {
            CloseLocator();
            _server = server;
            try
            {
                _socket = CreateSocket(server);
            }
            catch (SocketException e)
            {
                Fail(OnlineFailure.ServerUnreachable, "socket to " + server + ": " + e.SocketErrorCode);
                return false;
            }
            _net = new NetClient(_transport, _contentHash, _random, _reconnectSettings) { SocketFactory = _socketFactory };
            _lobby = new LobbyClient(_net);
            _match = new MatchClientSession(_net, _bundle, _matchSettings);
            _rejoinSaved = false;
            return true;
        }

        private IDatagramSocket CreateSocket(IPEndPoint server)
        {
            IDatagramSocket socket = new UdpDatagramSocket(server);
            Simulating = Debug.isDebugBuild && _config.DevSimEnabled;
            if (Simulating)
                socket = new LagSimulatorSocket(socket, _config.DevSimLatencyMs, _config.DevSimJitterMs, _config.DevSimLossPercent,
                    DevSimSlots, Environment.TickCount);
            return socket;
        }

        /// <summary>A new socket to the current server after the old one lost its path (§3.2); null if it cannot be made now.</summary>
        private IDatagramSocket CreateServerSocket()
        {
            if (_server == null) return null;
            try
            {
                IDatagramSocket socket = CreateSocket(_server);
                _socket = socket;
                UnityNetLog.Info("new socket to " + _server + " (the network changed)");
                return socket;
            }
            catch (SocketException e)
            {
                UnityNetLog.Warn("new socket to " + _server + ": " + e.SocketErrorCode);
                return null;
            }
        }

        private void Update()
        {
            long now = NetClock.NowMicros();
            if (_pings != null && _pings.IsRunning) UpdatePings(now);
            if (Phase == OnlinePhase.Locating) UpdateLocating(now);
            if (_net == null) return;

            _net.Update(now);
            _lobby.Update(now);
            switch (Phase)
            {
                case OnlinePhase.Connecting:
                case OnlinePhase.Searching:
                    if (_rejoining) UpdateRejoin();
                    else UpdateSearch();
                    break;
                case OnlinePhase.Starting:
                case OnlinePhase.InMatch:
                    UpdateMatch();
                    break;
            }
        }

        private void UpdatePings(long now)
        {
            _pings.Update(now);
            if (_pings.Runs == _pingRunsSeen) return;
            _pingRunsSeen = _pings.Runs;
            int best = _pings.BestRegion();
            if (!_regionChosen && best >= 0) _regionIndex = best;
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
                    Fail(FailureOf(_lobby.ConnectRejectReason, _lobby.DisconnectReason, _regionRemote), "connection failed: " + _lobby.ConnectRejectReason + " / " +
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
            SceneFlow.PlayMatch(new MatchLaunchParams(_fighterId, _bots, 0, true, _mode));
        }

        private void UpdateRejoin()
        {
            if (_match.HasStart)
            {
                Phase = OnlinePhase.Starting;
                int fighter = _match.Start.Slots[_match.OwnSlot].FighterIndex;
                string fighterId = fighter >= 0 && fighter < _bundle.Fighters.Length ? _bundle.Fighters[fighter].Id : _fighterId;
                if (_fighterId == null) _fighterId = fighterId;
                _mode = _match.Start.Mode;
                UnityNetLog.Info("back in the match: slot " + _match.OwnSlot + ", " + _mode);
                SceneFlow.PlayMatch(new MatchLaunchParams(fighterId, _bots, 0, true, _mode));
                return;
            }
            if (_net.Transport.State != ClientState.Disconnected) return;
            RejectReason reason = _net.Transport.RejectReason;
            if (reason == RejectReason.MatchOver || reason == RejectReason.InvalidToken)
            {
                ForgetTicket();
                Fail(OnlineFailure.MatchOver, "rejoin: the match is over");
                return;
            }
            Fail(FailureOf(reason, _net.Transport.DisconnectReason, false), "rejoin failed: " + reason + " / " + _net.Transport.DisconnectReason);
        }

        private void UpdateMatch()
        {
            if (_match.HasEnded && _rejoinSaved)
            {
                ForgetTicket();
                UnityNetLog.Info("match ended");
            }
            if (_net.ReconnectState == ReconnectState.MatchOver && _rejoinSaved)
            {
                ForgetTicket();
                UnityNetLog.Info("rejoin: the match is over");
            }
        }

        private void SaveRejoin()
        {
            if (_rejoinSaved || _lobby.State != LobbyState.Assigned) return;
            MatchAssignedMessage a = _lobby.Assignment;
            // An empty address means "this server" (S3.8): store the one we are connected to — after a restart it is unknown.
            NetAddress server = a.Address.IsEmpty && _server != null ? NetAddress.FromEndPoint(_server) : a.Address;
            var ticket = new RejoinTicket
            {
                Server = server, MatchId = a.MatchId, Slot = a.Slot, Token = a.RejoinToken, SavedAtSeconds = RejoinStore.UnixNowSeconds()
            };
            _rejoin.Save(in ticket);
            _ticket = ticket;
            _hasTicket = true;
            _rejoinSaved = true;
        }

        /// <summary>The app goes to the background or quits mid-match: the ticket's age restarts, so the return window counts from now.</summary>
        private void RefreshTicket()
        {
            if (!_rejoinSaved || !_hasTicket || _match == null || _match.HasEnded) return;
            _ticket.SavedAtSeconds = RejoinStore.UnixNowSeconds();
            _rejoin.Save(in _ticket);
        }

        private void ForgetTicket()
        {
            if (_rejoin != null) _rejoin.Clear();
            _hasTicket = false;
            _rejoinSaved = false;
        }

        /// <summary>
        /// Background and back (Server-Spec §7): nothing is sent while paused (no frames); on return the clocks of the match restart
        /// from the next snapshot instead of catching up, and a connection that died meanwhile returns by itself (NetClient).
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                RefreshTicket();
                return;
            }
            if (_match != null && (Phase == OnlinePhase.InMatch || Phase == OnlinePhase.Starting))
            {
                long now = NetClock.NowMicros();
                _match.OnResumed(now);
                UnityNetLog.Info("resumed: the match clocks restart");
            }
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            if (to.name == SceneNames.Match) return;
            LeaveMatch();
        }

        /// <summary>Leaves the current match (if any): <c>Leave</c>, close the connection, forget the rejoin ticket.</summary>
        private void LeaveMatch()
        {
            if (Phase != OnlinePhase.InMatch && Phase != OnlinePhase.Starting) return;
            if (_match != null && !_match.HasEnded) _match.Leave();
            if (_net != null) _net.Update(NetClock.NowMicros()); // flush the Leave
            ForgetTicket();
            Close();
            Phase = OnlinePhase.Idle;
            UnityNetLog.Info("left the match");
        }

        private static OnlineFailure FailureOf(RejectReason reason, DisconnectReason disconnect, bool remote)
        {
            // A remote server that never answered the handshake: most likely the network drops UDP (§12.4 "udp_blocked");
            // in the LAN the server was just found, so it is simply gone.
            if (reason == RejectReason.None && disconnect == DisconnectReason.ConnectTimeout && remote) return OnlineFailure.UdpBlocked;
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
            _rejoining = false;
        }
    }
}
