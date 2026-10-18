using Arena.Net.Client;
using Arena.Net.Transport;

namespace Arena.Tests.EditMode.Net.Lobby
{
    /// <summary>One client of a <see cref="LobbyRig"/>: the connection, its lobby and the match session waiting for <c>MatchStart</c>.</summary>
    public sealed class LobbyTestClient
    {
        public NetClient Net;
        public LobbyClient Lobby;
        public MatchClientSession Session;
        public InMemoryClientSocket Socket;

        /// <summary>The app is frozen: nothing is polled or sent (a dropped connection from the server's point of view).</summary>
        public bool Paused;
    }
}
