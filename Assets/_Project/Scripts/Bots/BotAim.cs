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
    }
}
