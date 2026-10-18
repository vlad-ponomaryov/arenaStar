namespace Arena.Net.Transport
{
    /// <summary>State of a <see cref="NetConnection"/> slot.</summary>
    public enum ConnectionState : byte
    {
        Free = 0,
        Connected = 1,

        /// <summary>Closed by the application or the transport; the slot is freed at the end of the current update.</summary>
        Closing = 2,
    }
}
