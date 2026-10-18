namespace Arena.Net.Client
{
    /// <summary>The ping badge (Server-Spec §6.3, GDD §8.2): green, yellow, red, or no answer.</summary>
    public enum PingQuality : byte
    {
        Unknown = 0,
        Good = 1,
        Fair = 2,
        Poor = 3,
    }
}
