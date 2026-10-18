namespace Arena.Net.Transport
{
    /// <summary>
    /// The server's single unconnected datagram socket (Server-Spec §3.2): non-blocking receive with the sender's address and
    /// send to an address, from the game thread. Implementations: <c>UdpServerSocket</c> (.NET 8+, no allocations),
    /// <c>PortableUdpServerSocket</c> (.NET Standard 2.1, allocates per packet), <see cref="InMemorySocket"/> (tests).
    /// </summary>
    public interface IServerDatagramSocket
    {
        /// <summary>False when nothing is pending. Oversized datagrams and ICMP resets are counted and skipped.</summary>
        bool TryReceive(byte[] buffer, out int length, out NetAddress from);

        bool SendTo(byte[] buffer, int offset, int length, in NetAddress to);

        /// <summary>ICMP "port unreachable" resets and failed sends skipped so far (metrics).</summary>
        long ResetCount { get; }

        /// <summary>Datagrams larger than the receive buffer skipped so far (metrics).</summary>
        long OversizeCount { get; }

        void Close();
    }
}
