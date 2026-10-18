namespace Arena.Net.Protocol
{
    /// <summary>What the client connects for (Server-Spec §3.2). Values are part of the wire format.</summary>
    public enum ConnectPurpose : byte
    {
        Lobby = 0,
        Match = 1,
        Rejoin = 2,
    }
}
