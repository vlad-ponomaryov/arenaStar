using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Random;

namespace Arena.Bots
{
    /// <summary>
    /// Human-like aim of a bot (GDD §7): the exact direction to the target rotated by a uniform random angle within
    /// ±spread (the bot's own RNG, so a seed reproduces the match). Lead on moving targets — by the difficulty (F1).
    /// </summary>
    public static class BotAim
    {
        /// <summary>Unit <paramref name="dir"/> rotated by a random angle in [−spread, +spread) degrees; zero stays zero.</summary>
        public static Vec2 Apply(Vec2 dir, float spreadDegrees, ref DeterministicRng rng)
        {
            Vec2 unit = dir.Normalized;
            if (spreadDegrees <= 0f || unit.IsZero) return unit;
            return MathUtil.Rotate(unit, rng.Range(-spreadDegrees, spreadDegrees)).Normalized;
        }

        /// <summary>
        /// Where to aim at a moving target (F1): for a <see cref="DeliveryKind.Projectile"/> ability the target position after the
        /// flight time to it, scaled by <paramref name="leadFraction"/>; other deliveries (and lead 0) — the target itself.
        /// </summary>
        public static Vec2 LeadPoint(Vec2 from, Vec2 targetPosition, Vec2 targetVelocity, AbilityDef ability, float leadFraction)
        {
            if (leadFraction <= 0f || ability == null || ability.Delivery != DeliveryKind.Projectile || ability.ProjectileSpeed <= 0f)
                return targetPosition;
            float flightSeconds = Vec2.Distance(from, targetPosition) / ability.ProjectileSpeed;
            return targetPosition + targetVelocity * (flightSeconds * leadFraction);
        }

        /// <summary>
        /// Aim length for <paramref name="ability"/>: a lob lands at aim length × range (D-006), so the unit aim is shortened to
        /// <paramref name="distance"/> / range (at least a tiny length — zero means "no manual aim"); other abilities keep the unit aim.
        /// </summary>
        public static Vec2 ForAbility(Vec2 unitAim, AbilityDef ability, float distance)
        {
            if (ability == null || ability.Delivery != DeliveryKind.Lob || ability.Range <= 0f || unitAim.IsZero) return unitAim;
            float k = MathUtil.Clamp(distance / ability.Range, MathUtil.DistanceEpsilon, 1f);
            return unitAim * k;
        }
    }
}
