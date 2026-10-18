using Arena.Net.Protocol;
using Arena.Net.Transport;

namespace Arena.Tests.EditMode.Net.Transport
{
    /// <summary>
    /// A server and clients on one <see cref="InMemoryNetwork"/> with virtual time (Server-Spec §11.1). Addresses: server
    /// 10.0.0.1:7777, client k — 10.0.1.k:5000+k. Every step advances the clock and updates all ends in a fixed order.
    /// </summary>
    public sealed class TransportRig
    {
        public const ulong ContentHash = 0x1122334455667788ul;
        public const long StepMicros = 1000000L / 60L;

        public readonly InMemoryNetwork Network;
        public readonly TransportSettings Settings;
        public readonly InMemorySocket ServerSocket;
        public readonly ServerTransport Server;
        public readonly RecordingHandler ServerHandler = new RecordingHandler();
        public static readonly NetAddress ServerAddress = NetAddress.FromIPv4(10, 0, 0, 1, 7777);

        private ClientTransport[] _clients = new ClientTransport[0];

        public TransportRig(int seed = 1, TransportSettings settings = null)
        {
            Network = new InMemoryNetwork(seed);
            Settings = settings ?? TransportSettings.CreateDefault();
            ServerSocket = Network.CreateSocket(ServerAddress);
            Server = new ServerTransport(ServerSocket, Settings, ContentHash, new DeterministicNetRandom(seed + 100), ServerHandler);
        }

        public long Now => Network.NowMicros;

        public static NetAddress ClientAddress(int k, int generation = 0) =>
            NetAddress.FromIPv4(10, 0, (byte)(1 + generation), (byte)k, (ushort)(5000 + k));

        public ClientTransport AddClient(int k, out InMemoryClientSocket socket, out RecordingHandler handler,
            ulong contentHash = ContentHash, ConnectPurpose purpose = ConnectPurpose.Lobby, bool hasToken = false, NetToken token = default)
        {
            handler = new RecordingHandler();
            socket = Network.CreateClientSocket(ClientAddress(k), ServerAddress);
            var client = new ClientTransport(Settings, contentHash, new DeterministicNetRandom(k * 31 + 7), handler);
            client.Connect(socket, purpose, hasToken, token, Now);
            System.Array.Resize(ref _clients, _clients.Length + 1);
            _clients[_clients.Length - 1] = client;
            return client;
        }

        /// <summary>One step: time += <paramref name="micros"/>, server receive, clients update, server update.</summary>
        public void Step(long micros = StepMicros)
        {
            Network.Advance(micros);
            long now = Network.NowMicros;
            Server.Receive(now);
            for (int i = 0; i < _clients.Length; i++) _clients[i].Update(now);
            Server.Update(now);
        }

        public void Run(long micros)
        {
            long end = Now + micros;
            while (Now < end) Step();
        }

        /// <summary>Steps until every client is connected or failed (at most <paramref name="maxMicros"/>).</summary>
        public bool RunUntilConnected(long maxMicros = 6000000L)
        {
            long end = Now + maxMicros;
            while (Now < end)
            {
                bool pending = false;
                for (int i = 0; i < _clients.Length; i++)
                    if (_clients[i].State == ClientState.Requesting || _clients[i].State == ClientState.Responding) pending = true;
                if (!pending) return true;
                Step();
            }
            return false;
        }
    }
}
