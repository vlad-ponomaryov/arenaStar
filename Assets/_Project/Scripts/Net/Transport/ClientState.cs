namespace Arena.Net.Transport
{
    /// <summary>State of <see cref="ClientTransport"/>.</summary>
    public enum ClientState : byte
    {
        Idle = 0,

        /// <summary>Sending padded <c>ConnectRequest</c>s, waiting for the challenge.</summary>
        Requesting = 1,

        /// <summary>Sending padded <c>ConnectResponse</c>s with the cookie, waiting for the accept.</summary>
        Responding = 2,

        Connected = 3,

        /// <summary>Ended; see <see cref="ClientTransport.DisconnectReason"/> (and the reject reason if refused).</summary>
        Disconnected = 4,
    }
}
