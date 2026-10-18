using System.Net;
using Arena.Core.Modes;
using Arena.Net.Client;
using Arena.Net.Transport;

namespace Arena.LoadTest
{
    /// <summary>One light load client: its own UDP socket, <see cref="NetClient"/> (no automatic return), lobby search and the match side.</summary>
    public sealed class LoadClient
    {
        public LoadClient(TransportSettings transport, ulong contentHash, IPEndPoint server, int salt)
        {
            Net = new NetClient(transport, contentHash, new CryptoNetRandom()) { ReconnectEnabled = false };
            Match = new LoadMatchClient(Net, salt); // before the search: MatchAssigned and MatchStart may share a packet
            Lobby = new LobbyClient(Net);
            Socket = new UdpDatagramSocket(server);
        }

        public NetClient Net { get; }
        public LobbyClient Lobby { get; }
        public LoadMatchClient Match { get; }
        public UdpDatagramSocket Socket { get; }

        /// <summary>Set by the ramp (main thread); the client thread starts the search on its next pass.</summary>
        public volatile bool SearchWanted;

        public ModeKind Mode;
        public int FighterIndex;
        private bool _searchStarted;

        /// <summary>One pass of the client thread.</summary>
        public void Update(long nowMicros)
        {
            if (!_searchStarted)
            {
                if (!SearchWanted) return;
                _searchStarted = true;
                Lobby.Search(Socket, Mode, FighterIndex, nowMicros);
            }
            Net.Update(nowMicros);
            Lobby.Update(nowMicros);
            Match.Update(nowMicros);
        }

        public long PacketsSent => Net.Transport.Connection?.PacketsSent ?? 0;
        public long PacketsReceived => Net.Transport.Connection?.PacketsReceived ?? 0;
        public long BytesSent => Net.Transport.Connection?.BytesSent ?? 0;
        public long BytesReceived => Net.Transport.Connection?.BytesReceived ?? 0;
    }
}
