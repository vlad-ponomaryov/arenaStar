namespace Arena.Gameplay.View
{
    /// <summary>Kind of a floor detail (A.3e, <see cref="FloorDecorPlanner"/>).</summary>
    public enum FloorDecorKind : byte
    {
        /// <summary>Flat stepping stone of a path.</summary>
        Stone = 0,
        /// <summary>Small loose pebble.</summary>
        Pebble = 1,
        /// <summary>Three-leaf sprout.</summary>
        Sprout = 2,
        /// <summary>Five-petal flower lying on the sand (<see cref="FloorDecorItem.Variant"/> = colour).</summary>
        Flower = 3
    }
}
