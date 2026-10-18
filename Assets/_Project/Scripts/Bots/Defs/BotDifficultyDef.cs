using System;

namespace Arena.Bots.Defs
{
    /// <summary>
    /// One bot skill level (GDD §7 table), converted from <c>BotDifficultyConfig</c>: aim spread and reaction delay in ticks.
    /// Ult/trick chances and "human mistakes" are added in S2.9.
    /// </summary>
    public sealed class BotDifficultyDef
    {
        public BotDifficulty Difficulty;

        /// <summary>Each shot is rotated by a uniform random angle in [−spread, +spread] degrees (Novice 25, Easy 15).</summary>
        public float AimSpreadDegrees;

        /// <summary>Ticks from first perceiving an enemy until the bot may shoot at it (Novice 600 ms = 18).</summary>
        public int ReactionTicks;

        public void Validate()
        {
            if (AimSpreadDegrees < 0f || AimSpreadDegrees > 180f)
                throw new ArgumentException("BotDifficultyDef " + Difficulty + ": AimSpreadDegrees must be in [0, 180]");
            if (ReactionTicks < 0) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": ReactionTicks must be ≥ 0");
        }
    }
}
