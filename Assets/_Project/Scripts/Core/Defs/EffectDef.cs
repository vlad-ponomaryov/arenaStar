using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// One effect of an ability (D-011), already in ticks / cells / int. Immutable by convention during the match.
    /// </summary>
    public sealed class EffectDef
    {
        public EffectKind Kind;
        public TargetFilter Filter;

        /// <summary>Amount at level 1: damage/heal in health points, multipliers in percent (S2.4).</summary>
        public int Value;

        /// <summary>Duration of timed effects (stun, slow…, S2.4); 0 for instant effects.</summary>
        public int DurationTicks;

        /// <summary>Distance in cells for movement effects (knockback, S2.4); 0 otherwise.</summary>
        public float Distance;

        public void Validate(string owner)
        {
            if (Kind == EffectKind.None) throw new ArgumentException(owner + ": effect kind is None");
            if (Filter == TargetFilter.None) throw new ArgumentException(owner + ": effect " + Kind + " has no target filter");
            if (Value < 0) throw new ArgumentException(owner + ": effect " + Kind + " value must be ≥ 0");
            if (DurationTicks < 0) throw new ArgumentException(owner + ": effect " + Kind + " duration must be ≥ 0");
            if (Distance < 0f) throw new ArgumentException(owner + ": effect " + Kind + " distance must be ≥ 0");
        }
    }
}
