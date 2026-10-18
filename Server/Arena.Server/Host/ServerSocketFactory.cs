using System.Net;
using Arena.Net.Transport;

namespace Arena.Server.Host
{
    /// <summary>
    /// Picks the server socket for this build (Server-Spec §3.2, §5.2): <see cref="UdpServerSocket"/> (no allocations) in the
    /// .NET 8+ SDK build, <see cref="Arena.Net.Server.Transport.PortableUdpServerSocket"/> in the bootstrap build.
    /// </summary>
    public static class ServerSocketFactory
    {
        public static IServerDatagramSocket Create(IPEndPoint local, out IPEndPoint bound)
        {
#if NET8_0_OR_GREATER
            var socket = new UdpServerSocket(local);
#else
            var socket = new Arena.Net.Server.Transport.PortableUdpServerSocket(local);
#endif
            bound = socket.LocalEndPoint;
            return socket;
        }

        /// <summary>Name of the socket type <see cref="Create"/> returns (for logs).</summary>
        public static string Kind
        {
            get
            {
#if NET8_0_OR_GREATER
                return "UdpServerSocket";
#else
                return "PortableUdpServerSocket";
#endif
            }
        }
    }
}
