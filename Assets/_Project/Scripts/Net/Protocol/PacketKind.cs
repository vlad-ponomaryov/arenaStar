namespace Arena.Net.Protocol
{
    /// <summary>
    /// Transport packet kind: the low 4 bits of the first header byte (Server-Spec §3.2). Kinds below <see cref="Data"/>
    /// are connectionless (connectionId = 0); the rest belong to a connection. 0 is invalid, so a zeroed buffer is dropped.
    /// </summary>
    public enum PacketKind : byte
    {
        Invalid = 0,
        ConnectRequest = 1,
        ConnectChallenge = 2,
        ConnectResponse = 3,
        ConnectAccepted = 4,
        ConnectRejected = 5,
        Ping = 6,
        Pong = 7,
        Discover = 8,
        DiscoverReply = 9,
        Data = 10,
        Disconnect = 11,
        PathChallenge = 12,
        PathResponse = 13,
    }
}
