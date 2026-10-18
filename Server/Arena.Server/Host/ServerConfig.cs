using System;
using Arena.Net.Server.Match;
using Arena.Net.Transport;

namespace Arena.Server.Host
{
    /// <summary>
    /// Everything <c>server.json</c> sets (Server-Spec §5.5): <c>"Host"</c> — the process, <c>"Server"</c> — matches, lobby and
    /// snapshots (<see cref="ServerSettings"/> with <c>"Snapshots"</c>), <c>"Transport"</c> — <see cref="TransportSettings"/>.
    /// Keys left out keep the PROTO defaults of §13.
    /// </summary>
    public sealed class ServerConfig
    {
        public HostSettings Host = HostSettings.CreateDefault();
        public ServerSettings Server = ServerSettings.CreateDefault();
        public TransportSettings Transport = TransportSettings.CreateDefault();

        /// <summary>Folder of the config file (the maintenance flag lives there); null — defaults without a file.</summary>
        public string Directory;

        public static ServerConfig CreateDefault() => new ServerConfig();

        public void Validate()
        {
            if (Host == null || Server == null || Transport == null) throw new ArgumentException("ServerConfig: a section is missing");
            Host.Validate();
            Server.Validate();
            if (Transport.MaxConnections < 1) throw new ArgumentException("Transport.MaxConnections must be >= 1");
            if (Transport.DisconnectTimeoutMs <= Transport.KeepaliveMs) throw new ArgumentException("Transport: DisconnectTimeoutMs must exceed KeepaliveMs");
        }
    }
}
