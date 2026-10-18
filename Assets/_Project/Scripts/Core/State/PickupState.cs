using System;
using Arena.Core.Math;

namespace Arena.Core.State
{
    /// <summary>
    /// An item lying on the map (an acorn of the Acorns mode, a berry of the Last Beast mode) in <see cref="MatchState.Pickups"/> (D-002).
    /// When adding a field: also add it to <see cref="Equals(PickupState)"/>.
    /// </summary>
    public struct PickupState : IEquatable<PickupState>
    {
        public bool Active;

        /// <summary>Acorn (default) or berry.</summary>
        public PickupKind Kind;

        /// <summary>Acorns (berries) it is worth: 1 for a normal acorn, <c>AcornsModeDef.GoldenValue</c> (3) for a golden one.</summary>
        public int Value;

        public Vec2 Position;

        /// <summary>First tick (time at the end of a tick) when it can be picked up (dropped acorns wait <c>PickupDelayTicks</c>).</summary>
        public int PickableFromTick;

        /// <summary>Time it appeared (views tell a reused slot from the old item by it).</summary>
        public int SpawnTick;

        public bool Equals(PickupState o) => Active == o.Active && Kind == o.Kind && Value == o.Value && Position == o.Position
            && PickableFromTick == o.PickableFromTick && SpawnTick == o.SpawnTick;

        public override bool Equals(object obj) => obj is PickupState other && Equals(other);
        public override int GetHashCode() => (Value * 397) ^ Position.GetHashCode() ^ SpawnTick;
    }
}
