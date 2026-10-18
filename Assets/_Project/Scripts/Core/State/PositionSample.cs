using System;
using Arena.Core.Math;

namespace Arena.Core.State
{
    /// <summary>
    /// One entry of <see cref="MatchState.PositionHistory"/> (lag compensation, Server-Spec §2.8): where a fighter slot was at the
    /// end of a tick and whether it was in play (active and alive). When adding a field: also add it to <see cref="Equals(PositionSample)"/>.
    /// </summary>
    public struct PositionSample : IEquatable<PositionSample>
    {
        public Vec2 Position;

        /// <summary>Active and alive at the end of the tick.</summary>
        public bool Alive;

        public bool Equals(PositionSample o) => Position == o.Position && Alive == o.Alive;

        public override bool Equals(object obj) => obj is PositionSample other && Equals(other);
        public override int GetHashCode() => (Position.GetHashCode() * 397) ^ (Alive ? 1 : 0);
    }
}
