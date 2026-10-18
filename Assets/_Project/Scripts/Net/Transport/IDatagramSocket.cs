namespace Arena.Net.Transport
{
    /// <summary>
    /// A connected datagram socket of the client (Server-Spec §3.2): one remote peer, polled without blocking from the main
    /// thread, into preallocated buffers. Never throws in normal operation; errors (reset, unreachable network) only raise
    /// <see cref="PathBroken"/> — the signal to recreate the socket ("Смена адреса").
    /// </summary>
    public interface IDatagramSocket
    {
        /// <summary>False if the datagram could not be sent (the path is broken or the send buffer is full).</summary>
        bool Send(byte[] buffer, int offset, int length);

        /// <summary>
        /// Takes the next pending datagram into <paramref name="buffer"/> (at least <see cref="NetLimits.ReceiveBufferBytes"/>).
        /// False when nothing is pending. Oversized datagrams and ICMP resets are skipped.
        /// </summary>
        bool TryReceive(byte[] buffer, out int length);

        /// <summary>A send or receive failed in a way that means the local path no longer works.</summary>
        bool PathBroken { get; }

        void Close();
    }
}
