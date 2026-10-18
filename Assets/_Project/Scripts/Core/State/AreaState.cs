using System;
using Arena.Core.Math;

namespace Arena.Core.State
{
    /// <summary>
    /// A delayed circular hit zone of <see cref="MatchState.Areas"/> (D-002): applies its ability's effects once at
    /// <see cref="ActivateTick"/> to every matching fighter inside, disappears at <see cref="ExpireTick"/>.
    /// For lobbed bombs and placed zones (S2.11+); GDD §5.3: area attacks show the zone 0.5 s before triggering.
    /// When adding a field: also add it to <see cref="Equals(AreaState)"/> (StateEqualityTests).
    /// </summary>
    public struct AreaState : IEquatable<AreaState>
    {
        public bool Active;

        /// <summary>Effects already applied (activation happened).</summary>
        public bool Triggered;

        public int Owner;
        public int Team;
        public Vec2 Position;
        public float Radius;
        public int ActivateTick;
        public int ExpireTick;
        public int AbilityRef;

        public bool Equals(AreaState o)
        {
            return Active == o.Active && Triggered == o.Triggered && Owner == o.Owner && Team == o.Team
                && Position == o.Position && Radius == o.Radius && ActivateTick == o.ActivateTick
                && ExpireTick == o.ExpireTick && AbilityRef == o.AbilityRef;
        }

        public override bool Equals(object obj) => obj is AreaState other && Equals(other);
        public override int GetHashCode() => (Owner * 397) ^ ActivateTick ^ Position.GetHashCode();
    }
}
