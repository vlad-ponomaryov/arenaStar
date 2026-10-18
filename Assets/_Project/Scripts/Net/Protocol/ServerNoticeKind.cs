namespace Arena.Net.Protocol
{
    /// <summary>What <see cref="ServerNoticeMessage"/> announces (Server-Spec §4.2, §5.3). Wire format.</summary>
    public enum ServerNoticeKind : byte
    {
        None = 0,

        /// <summary>Maintenance: no new matches, running matches play to the end.</summary>
        Maintenance = 1,

        /// <summary>The server stops: running matches play to the end within <c>DrainSeconds</c> (S3.9).</summary>
        Shutdown = 2,
    }
}
