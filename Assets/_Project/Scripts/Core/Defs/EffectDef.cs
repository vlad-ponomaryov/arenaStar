using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// One effect of an ability (D-011), already in ticks / cells / int. Immutable by convention during the match.
    /// Meaning of the fields per kind: see <see cref="EffectKind"/>.
    /// </summary>
    public sealed class EffectDef
    {
        public EffectKind Kind;
        public TargetFilter Filter;

        /// <summary>Damage/heal in health points at level 1; percent for <see cref="EffectKind.Slow"/>,
        /// <see cref="EffectKind.DamageReduction"/> and <see cref="EffectKind.BoostShots"/>; unused otherwise.</summary>
        public int Value;

        /// <summary>Duration of timed effects (stun, slow, …) and the push time of a knockback; 0 for instant effects.</summary>
        public int DurationTicks;

        /// <summary>Distance in cells for <see cref="EffectKind.Knockback"/>; 0 otherwise.</summary>
        public float Distance;

        /// <summary>Number of boosted attack activations for <see cref="EffectKind.BoostShots"/>; 0 otherwise.</summary>
        public int Count;

        public void Validate(string owner)
        {
            if (Kind == EffectKind.None) throw new ArgumentException(owner + ": effect kind is None");
            if (Filter == TargetFilter.None) throw new ArgumentException(owner + ": effect " + Kind + " has no target filter");
            if (Value < 0) throw new ArgumentException(owner + ": effect " + Kind + " value must be ≥ 0");
            if (DurationTicks < 0) throw new ArgumentException(owner + ": effect " + Kind + " duration must be ≥ 0");
            if (Distance < 0f) throw new ArgumentException(owner + ": effect " + Kind + " distance must be ≥ 0");
            if (Count < 0) throw new ArgumentException(owner + ": effect " + Kind + " count must be ≥ 0");
            switch (Kind)
            {
                case EffectKind.Damage:
                case EffectKind.Heal:
                    break;
                case EffectKind.Knockback:
                    if (Distance <= 0f) throw new ArgumentException(owner + ": knockback distance must be > 0");
                    break;
                case EffectKind.Slow:
                case EffectKind.DamageReduction:
                    if (Value < 1 || Value > 100) throw new ArgumentException(owner + ": effect " + Kind + " percent must be in [1, 100]");
                    RequireDuration(owner);
                    break;
                case EffectKind.Stun:
                case EffectKind.Invisibility:
                case EffectKind.Mark:
                    RequireDuration(owner);
                    break;
                case EffectKind.TeamReveal:
                    RequireDuration(owner);
                    if (Filter != TargetFilter.Self) throw new ArgumentException(owner + ": a team reveal is applied to Self");
                    break;
                case EffectKind.BoostShots:
                    if (Value < 1) throw new ArgumentException(owner + ": a shot boost needs Value ≥ 1 %");
                    if (Count < 1) throw new ArgumentException(owner + ": a shot boost needs Count ≥ 1");
                    break;
                default:
                    throw new ArgumentException(owner + ": unsupported effect kind " + Kind);
            }
        }

        private void RequireDuration(string owner)
        {
            if (DurationTicks <= 0) throw new ArgumentException(owner + ": effect " + Kind + " needs a duration > 0");
        }
    }
}
