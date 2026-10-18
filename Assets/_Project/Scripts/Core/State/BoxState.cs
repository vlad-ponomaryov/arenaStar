using System;

namespace Arena.Core.State
{
    /// <summary>
    /// A berry box of the Last Beast mode in <see cref="MatchState.Boxes"/> (D-002, Server-Spec §9.2): an entity on top of a
    /// <c>TileType.BerryBox</c> tile with its durability. At 0 the tile becomes floor and berries drop (<c>BoxSystem</c>).
    /// When adding a field: also add it to <see cref="Equals(BoxState)"/>.
    /// </summary>
    public struct BoxState : IEquatable<BoxState>
    {
        public bool Active;

        /// <summary>Tile index of the box cell (row × map width + column).</summary>
        public int Cell;

        /// <summary>Durability left.</summary>
        public int Health;

        public bool Equals(BoxState o) => Active == o.Active && Cell == o.Cell && Health == o.Health;

        public override bool Equals(object obj) => obj is BoxState other && Equals(other);
        public override int GetHashCode() => (Cell * 397) ^ Health;
    }
}
