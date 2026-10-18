using Arena.Net.Transport;

namespace Arena.Net.Server.Transport
{
    /// <summary>
    /// A real server socket the game thread can sleep on (Server-Spec §5.3): <see cref="WaitReadable"/> blocks until a datagram
    /// is pending or the timeout passes (<c>Socket.Poll</c>) — the host waits for the next tick deadline and still wakes on packets.
    /// </summary>
    public interface IWaitableServerSocket : IServerDatagramSocket
    {
        /// <summary>True — a datagram is pending; false — timed out (or the socket is closed). Never throws.</summary>
        bool WaitReadable(int timeoutMicros);
    }
}
