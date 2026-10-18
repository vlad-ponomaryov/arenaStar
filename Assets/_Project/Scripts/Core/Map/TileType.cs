namespace Arena.Core.Map
{
    /// <summary>
    /// Kind of one map cell (GDD §5.5). Stored as a byte in <c>MatchState.Tiles</c>; append new kinds at the end.
    /// Movement / projectile rules per kind — <see cref="TileRules"/>.
    /// </summary>
    public enum TileType : byte
    {
        Floor = 0,

        /// <summary>Blocks movement and projectiles.</summary>
        Wall = 1,

        /// <summary>Like a wall, but abilities with <c>BreaksWalls</c> turn it into floor.</summary>
        BreakableWall = 2,

        /// <summary>Walkable; hides a fighter standing in it (D-024).</summary>
        Bush = 3,

        /// <summary>Blocks movement; projectiles fly over it.</summary>
        Water = 4,

        /// <summary>Box with power berries: blocks like a wall; in Last Beast it has durability and breaks into floor (<c>BoxSystem</c>).</summary>
        BerryBox = 5
    }
}
