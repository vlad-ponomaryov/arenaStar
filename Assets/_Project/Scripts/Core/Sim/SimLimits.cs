namespace Arena.Core.Sim
{
    /// <summary>
    /// Compile-time capacities of the simulation state (D-004). Technical limits, not gameplay parameters:
    /// they must be constants so that networked arrays (Fusion [Capacity(N)], stage 3) can use them.
    /// </summary>
    public static class SimLimits
    {
        public const int TickRate = 30;
        public const int MaxFighters = 10;
        public const int MaxProjectiles = 256;
        public const int MaxAreas = 64;
        public const int MaxStatusEffects = 128;
        public const int MaxPickups = 64;
        public const int MaxEvents = 256;
    }
}
