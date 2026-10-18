using System;
using Arena.Core.Defs;
using Arena.Core.Math;

namespace Arena.Core.State
{
    /// <summary>
    /// A circular zone of <see cref="MatchState.Areas"/> (D-002) created by a lob or a placed ability; behaviour by
    /// <see cref="Kind"/> (<see cref="AreaKind"/>): a burst applies its ability's effects once at <see cref="ActivateTick"/>,
    /// a mine waits for an enemy until <see cref="ExpireTick"/>, a field pulses until <see cref="ExpireTick"/>.
    /// GDD §5.3: area attacks show the zone 0.5 s before triggering (the view telegraphs bursts).
    /// When adding a field: also add it to <see cref="Equals(AreaState)"/> (StateEqualityTests).
    /// </summary>
    public struct AreaState : IEquatable<AreaState>
    {
        public bool Active;

        /// <summary>Effects already applied (burst activated, mine set off).</summary>
        public bool Triggered;

        public AreaKind Kind;
        public int Owner;
        public int Team;
        public Vec2 Position;

        /// <summary>Where it was thrown from (the start of a lob's arc; = <see cref="Position"/> for placed areas).</summary>
        public Vec2 Origin;

        /// <summary>Tick of creation (start of a lob's flight; views tell a reused slot by it).</summary>
        public int SpawnTick;

        public float Radius;
        public int ActivateTick;
        public int ExpireTick;
        public int AbilityRef;

        /// <summary>Field: tick of the next effect pulse.</summary>
        public int NextPulseTick;

        /// <summary>Field: pulses done so far.</summary>
        public int PulseIndex;

        /// <summary>Bit per team that sees it (hidden mines: own team + enemies closer than the reveal distance; else all).</summary>
        public uint VisibleToTeams;

        public bool IsVisibleTo(int team) => (uint)team < 32u && (VisibleToTeams & (1u << team)) != 0;

        public bool Equals(AreaState o)
        {
            return Active == o.Active && Triggered == o.Triggered && Kind == o.Kind && Owner == o.Owner && Team == o.Team
                && Position == o.Position && Origin == o.Origin && SpawnTick == o.SpawnTick && Radius == o.Radius
                && ActivateTick == o.ActivateTick && ExpireTick == o.ExpireTick && AbilityRef == o.AbilityRef
                && NextPulseTick == o.NextPulseTick && PulseIndex == o.PulseIndex && VisibleToTeams == o.VisibleToTeams;
        }

        public override bool Equals(object obj) => obj is AreaState other && Equals(other);
        public override int GetHashCode() => (Owner * 397) ^ ActivateTick ^ Position.GetHashCode();
    }
}
