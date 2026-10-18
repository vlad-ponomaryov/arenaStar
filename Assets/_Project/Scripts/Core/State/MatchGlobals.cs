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

        /// <summary>Pickups (acorns) not placed because all <c>SimLimits.MaxPickups</c> slots were busy.</summary>
        public int DroppedPickups;

        /// <summary>Size of the map in <c>MatchState.Tiles</c> (copied from <c>MapDef</c> at reset).</summary>
        public int MapWidth;
        public int MapHeight;

        /// <summary>
        /// Prediction mode of the client's shadow simulation (Server-Spec §2.5, D-080): only <see cref="PredictedSlot"/> acts —
        /// input, statuses, movement, abilities, dashes, charges, respawn and visibility touch that fighter only; effects apply
        /// only from it to itself and never damage or heal; hook pulls, mines, boxes, regeneration, mode rules and the match RNG
        /// stand still. False (the default after <c>Clear()</c>) = the normal simulation, unchanged.
        /// </summary>
        public bool Predicting;

        /// <summary>The fighter slot that acts in prediction mode (valid only while <see cref="Predicting"/>).</summary>
        public int PredictedSlot;

        /// <summary>True if the systems run the fighter in <paramref name="slot"/> this tick (always, unless predicting another slot).</summary>
        public bool Simulates(int slot) => !Predicting || slot == PredictedSlot;

        public bool Equals(MatchGlobals o) => Tick == o.Tick && FighterCount == o.FighterCount && Seed == o.Seed && Rng.Equals(o.Rng)
            && DroppedProjectiles == o.DroppedProjectiles && DroppedAreas == o.DroppedAreas && DroppedStatuses == o.DroppedStatuses && DroppedPickups == o.DroppedPickups
            && MapWidth == o.MapWidth && MapHeight == o.MapHeight
            && Predicting == o.Predicting && PredictedSlot == o.PredictedSlot;
        public override bool Equals(object obj) => obj is MatchGlobals other && Equals(other);
        public override int GetHashCode() => (Tick * 397) ^ FighterCount ^ Rng.GetHashCode();
    }
}
