namespace Arena.Server.Host
{
    /// <summary>Level of <see cref="ConsoleLog"/> lines (Server-Spec §5.6); the config sets the lowest one printed.</summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }
}
