namespace Arena.Net.Transport
{
    /// <summary>
    /// Receives what arrives on a connection, synchronously while a packet is processed. The byte ranges are valid only during
    /// the call (copy what must outlive it). Implementations must not allocate on the hot path.
    /// </summary>
    public interface IConnectionHandler
    {
        /// <summary>A reliable message, in order, exactly once.</summary>
        void OnReliableMessage(int connection, byte[] buffer, int offset, int length);

        /// <summary>The unreliable payload of a new (not duplicate, not replayed) packet with this sequence.</summary>
        void OnPayload(int connection, ushort sequence, byte[] buffer, int offset, int length);

        /// <summary>One of our packets was acknowledged (snapshot baselines, S3.5).</summary>
        void OnPacketAcked(int connection, ushort sequence);
    }
}
