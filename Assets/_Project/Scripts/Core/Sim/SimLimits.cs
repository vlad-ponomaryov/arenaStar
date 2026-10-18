namespace Arena.Core.Sim
{
    /// <summary>
    /// Compile-time capacities of the simulation state (D-004). Technical limits, not gameplay parameters:
    /// they must be constants so that networked arrays (Fusion [Capacity(N)], stage 3) can use them.
    /// Overflow is handled without exceptions (the item is dropped and counted).
    /// </summary>
    public static class SimLimits
    {
        public const int TickRate = 30;
        public const int MaxFighters = 10;

        /// <summary>Teams per match: 2 in 3×3, up to one per fighter in the royale. Team masks are <c>uint</c> (≤ 32).</summary>
        public const int MaxTeams = MaxFighters;

        public const int MaxProjectiles = 256;
        public const int MaxAreas = 64;
        public const int MaxStatusEffects = 128;
        public const int MaxPickups = 64;
        public const int MaxEvents = 256;

        /// <summary>Tiles of the largest map: 45×45 royale (GDD §5.4) = 2025 ≤ 2048.</summary>
        public const int MaxMapCells = 2048;
    }
}
