using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Random;

namespace Arena.Bots
{
    /// <summary>
    /// Human-like aim of a bot (GDD §7): the exact direction to the target rotated by a uniform random angle within
    /// ±spread (the bot's own RNG, so a seed reproduces the match). No lead on moving targets.
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
