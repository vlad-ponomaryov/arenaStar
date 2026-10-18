namespace Arena.Net.Protocol
{
    /// <summary>Why a connection ended (the <c>Disconnect</c> packet and local events). Values are part of the wire format.</summary>
    public enum DisconnectReason : byte
    {
        None = 0,

        /// <summary>The peer closed the connection itself.</summary>
        Closed = 1,

        /// <summary>No packets for <c>DisconnectTimeoutMs</c>.</summary>
        Timeout = 2,

        /// <summary>Packet or byte limits were exceeded for too long (Server-Spec §3.2 "Лимиты").</summary>
        RateLimited = 3,

        /// <summary>The server is shutting down.</summary>
        ServerShutdown = 4,

        /// <summary>The client gave up connecting (no answer for <c>ConnectTimeoutMs</c>).</summary>
        ConnectTimeout = 5,

        /// <summary>The server refused the connection (see the reject reason).</summary>
        Rejected = 6,

        /// <summary>The local reliable buffer overflowed (more unacknowledged messages than the window).</summary>
        ReliableOverflow = 7,
    }
}
