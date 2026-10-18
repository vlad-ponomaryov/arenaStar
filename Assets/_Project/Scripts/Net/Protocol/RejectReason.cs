namespace Arena.Net.Protocol
{
    /// <summary>Why the server refused a connection (<c>ConnectRejected</c>, Server-Spec §3.2). Values are part of the wire format.</summary>
    public enum RejectReason : byte
    {
        None = 0,
        VersionMismatch = 1,
        ContentMismatch = 2,
        ServerFull = 3,
        Maintenance = 4,
        RateLimited = 5,
        InvalidToken = 6,
        MatchOver = 7,
    }
}
