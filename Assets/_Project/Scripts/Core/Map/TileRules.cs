namespace Arena.Core.Map
{
    /// <summary>What each <see cref="TileType"/> does (GDD §5.2): the single place for movement / projectile / bush rules.</summary>
    public static class TileRules
    {
        /// <summary>Fighters cannot enter: walls, breakable walls, water, berry boxes.</summary>
        public static bool BlocksMovement(TileType t) =>
            t == TileType.Wall || t == TileType.BreakableWall || t == TileType.Water || t == TileType.BerryBox;

        /// <summary>Straight projectiles stop here (water does not stop them).</summary>
        public static bool BlocksProjectiles(TileType t) =>
            t == TileType.Wall || t == TileType.BreakableWall || t == TileType.BerryBox;

        /// <summary>Broken by abilities with <c>BreaksWalls</c>.</summary>
        public static bool IsBreakable(TileType t) => t == TileType.BreakableWall;

        public static bool IsBush(TileType t) => t == TileType.Bush;
    }
}
