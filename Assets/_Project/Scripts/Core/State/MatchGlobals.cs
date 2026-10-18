using System;
using Arena.Core.Random;

namespace Arena.Core.State
{
    /// <summary>
    /// Match-wide scalar state (D-002): tick counter, deterministic RNG, counters of dropped overflow items.
    /// When adding a field: also add it to <see cref="Equals(MatchGlobals)"/> — StateEqualityTests checks every field.
    /// </summary>
    public struct MatchGlobals : IEquatable<MatchGlobals>
    {
        /// <summary>Number of completed ticks; the tick being simulated has this index.</summary>
        public int Tick;

        /// <summary>Number of used fighter slots (from MatchSetup).</summary>
        public int FighterCount;

        public int Seed;

        /// <summary>Match RNG — use by reference: <c>ref state.Globals.Rng</c>.</summary>
        public DeterministicRng Rng;

        /// <summary>Projectiles not fired because all <c>SimLimits.MaxProjectiles</c> slots were busy (D-004).</summary>
        public int DroppedProjectiles;

        /// <summary>Areas not created because all <c>SimLimits.MaxAreas</c> slots were busy.</summary>
        public int DroppedAreas;

        /// <summary>Status effects not applied because all <c>SimLimits.MaxStatusEffects</c> slots were busy.</summary>
        public int DroppedStatuses;

        public bool Equals(MatchGlobals o) => Tick == o.Tick && FighterCount == o.FighterCount && Seed == o.Seed && Rng.Equals(o.Rng)
            && DroppedProjectiles == o.DroppedProjectiles && DroppedAreas == o.DroppedAreas && DroppedStatuses == o.DroppedStatuses;
        public override bool Equals(object obj) => obj is MatchGlobals other && Equals(other);
        public override int GetHashCode() => (Tick * 397) ^ FighterCount ^ Rng.GetHashCode();
    }
}
