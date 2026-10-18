using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Arena.Net.Transport
{
    /// <summary>Platform settings shared by the UDP sockets (Server-Spec §3.2).</summary>
    public static class SocketSetup
    {
        /// <summary>WSAIOCTL SIO_UDP_CONNRESET.</summary>
        private const int SioUdpConnReset = unchecked((int)0x9800000C);

        /// <summary>
        /// On Windows an ICMP "port unreachable" from a client that went away makes the next <c>ReceiveFrom</c> fail with
        /// WSAECONNRESET; switching SIO_UDP_CONNRESET off removes that. No-op elsewhere. Call right after creating the socket.
        /// </summary>
        public static void DisableConnectionReset(Socket socket)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                socket.IOControl(SioUdpConnReset, new byte[4], null);
            }
            catch (SocketException)
            {
                // Not supported by this stack: resets are still counted and skipped by the receive loops.
            }
        }

        /// <summary>A send/receive error that means the local path is gone (the interface changed or went down).</summary>
        public static bool IsPathError(SocketError error) =>
            error == SocketError.NetworkUnreachable || error == SocketError.NetworkDown || error == SocketError.HostUnreachable
            || error == SocketError.AddressNotAvailable || error == SocketError.NotConnected || error == SocketError.Shutdown
            || error == SocketError.OperationAborted || error == SocketError.NotSocket;
    }
}
