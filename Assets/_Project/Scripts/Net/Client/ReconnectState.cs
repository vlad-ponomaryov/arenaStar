namespace Arena.Net.Client
{
    /// <summary>Where the automatic return to the match of a <see cref="NetClient"/> is (Server-Spec §7, S3.12).</summary>
    public enum ReconnectState : byte
    {
        /// <summary>Connected, or nothing to return to (no rejoin token, reconnecting off, the connection was closed on purpose).</summary>
        None = 0,

        /// <summary>The connection was lost: waiting for the next attempt.</summary>
        Waiting = 1,

        /// <summary>A <c>Rejoin</c> handshake is running.</summary>
        Connecting = 2,

        /// <summary>The window passed (or the server refused for good): no more automatic attempts — "Back to the match" may retry.</summary>
        GaveUp = 3,

        /// <summary>The server answered <c>MatchOver</c> (or does not know the ticket): the match is over, nothing to return to.</summary>
        MatchOver = 4,
    }
}
