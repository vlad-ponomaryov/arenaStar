using System;
using System.Collections.Generic;
using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Net.Client;
using Arena.Net.Content;
using Arena.Net.Server.Lobby;
using Arena.Net.Server.Match;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Net.Server;
using Arena.Tests.EditMode.Net.Snapshots;

namespace Arena.Tests.EditMode.Net.Lobby
{
    /// <summary>
    /// A <see cref="ServerNode"/> (lobby + matches) and lobby clients (<see cref="NetClient"/> + <see cref="LobbyClient"/> +
    /// <see cref="MatchClientSession"/>) on one <see cref="InMemoryNetwork"/> with virtual time (Server-Spec §11.1 "Лобби", S3.8).
    /// A frame is 1/60 s: the clock advances, the node runs, then every client that is not paused: transport → lobby →
    /// match session (zero input, so it answers <c>ClientReady</c> and sends inputs once the match starts).
    /// </summary>
    public sealed class LobbyRig
    {
        public const long FrameMicros = 1000000L / 60L;
        public const ulong ContentHash = 0x5EEDu;
        public static readonly NetAddress ServerAddress = MatchServerRig.ServerAddress;

        public readonly InMemoryNetwork Network;
        public readonly ContentBundle Bundle;
        public readonly TransportSettings Transport;
        public readonly ServerSettings Settings;
        public readonly ServerNode Node;
        public readonly List<LobbyTestClient> Clients = new List<LobbyTestClient>();

        private readonly int _seed;

        public LobbyRig(int seed = 7, NetworkConditions conditions = null, ServerSettings settings = null)
        {
            _seed = seed;
            Network = new InMemoryNetwork(seed);
            Network.DefaultConditions = conditions ?? new NetworkConditions();
            Bundle = SnapshotMatchRig.LoadBundle();
            Transport = TransportSettings.CreateDefault();
            Settings = settings ?? MatchServerRig.CreateSettings();
            Node = new ServerNode(Network.CreateSocket(ServerAddress), Transport, Settings, Bundle, ContentHash,
                new DeterministicNetRandom(seed + 1));
        }

        public long Now => Network.NowMicros;

        public Matchmaker Matchmaker => Node.Matchmaker;

        public LobbyMetrics Metrics => Node.LobbyMetrics;

        public LobbyTestClient AddClient(ulong contentHash = ContentHash)
        {
            int k = Clients.Count;
            var net = new NetClient(Transport, contentHash, new DeterministicNetRandom(_seed * 31 + k));
            var client = new LobbyTestClient
            {
                Net = net,
                Lobby = new LobbyClient(net),
                Session = new MatchClientSession(net, Bundle),
                Socket = Network.CreateClientSocket(NetAddress.FromIPv4(10, 0, (byte)(1 + k / 200), (byte)(1 + k % 200), (ushort)(5000 + k)),
                    ServerAddress),
            };
            Clients.Add(client);
            return client;
        }

        /// <summary>Adds <paramref name="count"/> clients that search <paramref name="mode"/> now; fighter of client k — <paramref name="fighters"/>[k] or k mod N.</summary>
        public LobbyTestClient[] AddSearching(int count, ModeKind mode, int[] fighters = null)
        {
            var added = new LobbyTestClient[count];
            for (int k = 0; k < count; k++)
            {
                added[k] = AddClient();
                int fighter = fighters != null ? fighters[k] : k % Bundle.Fighters.Length;
                added[k].Lobby.Search(added[k].Socket, mode, fighter, Now);
            }
            return added;
        }

        public void Step()
        {
            Network.Advance(FrameMicros);
            long now = Network.NowMicros;
            Node.Update(now);
            for (int i = 0; i < Clients.Count; i++)
            {
                LobbyTestClient c = Clients[i];
                if (c.Paused) continue;
                c.Net.Update(now);
                c.Lobby.Update(now);
                int ticks = c.Session.BeginFrame(now);
                PlayerInput input = default;
                for (int t = 0; t < ticks; t++) c.Session.Step(in input, now);
            }
        }

        public bool RunUntil(Func<bool> done, float maxSeconds)
        {
            long end = Now + (long)(maxSeconds * 1000000.0);
            while (Now < end)
            {
                if (done()) return true;
                Step();
            }
            return done();
        }

        public void RunSeconds(float seconds)
        {
            long end = Now + (long)(seconds * 1000000.0);
            while (Now < end) Step();
        }

        public static bool All(LobbyTestClient[] clients, Func<LobbyTestClient, bool> predicate)
        {
            for (int i = 0; i < clients.Length; i++)
                if (!predicate(clients[i])) return false;
            return true;
        }

        /// <summary>Open rooms of the node.</summary>
        public List<MatchRoom> Rooms()
        {
            var rooms = new List<MatchRoom>();
            for (int i = 0; i < Node.Matches.RoomCapacity; i++)
                if (Node.Matches.GetRoom(i) != null) rooms.Add(Node.Matches.GetRoom(i));
            return rooms;
        }
    }
}
