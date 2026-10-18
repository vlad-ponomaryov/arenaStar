using System;

namespace Arena.Bots.Defs
{
    /// <summary>
    /// One bot skill level (GDD §7 table), converted from <c>BotDifficultyConfig</c>: aim spread and reaction delay in ticks.
    /// Ult / trick chances and "human mistakes" (S2.9): chances are per decision (0.2 s) while the situation fits.
    /// </summary>
    public sealed class BotDifficultyDef
    {
        public BotDifficulty Difficulty;

        /// <summary>Each shot is rotated by a uniform random angle in [−spread, +spread] degrees (Novice 25, Easy 15).</summary>
        public float AimSpreadDegrees;

        /// <summary>Ticks from first perceiving an enemy until the bot may shoot at it (Novice 600 ms = 18).</summary>
        public int ReactionTicks;

        /// <summary>Chance to use a charged ult when it is worth it (<c>UltDecision.IsWorthIt</c>).</summary>
        public float UltUseChance;

        /// <summary>Chance to use a charged ult on a target in its zone when it is not worth it ("off the mark").</summary>
        public float UltMisuseChance;

        /// <summary>Chance to use the trick when the fighter's <c>BotTrickHint</c> fits (<c>TrickDecision</c>).</summary>
        public float TrickUseChance;

        /// <summary>Chance to use the trick off the mark while an enemy is perceived.</summary>
        public float TrickMisuseChance;

        /// <summary>Chance to shoot into a random bush in range although nobody was seen there (a human mistake).</summary>
        public float EmptyBushShotChance;

        /// <summary>The retreat threshold is shifted per life by a uniform random amount in [−jitter, +jitter].</summary>
        public float RetreatThresholdJitter;

        public void Validate()
        {
            if (AimSpreadDegrees < 0f || AimSpreadDegrees > 180f)
                throw new ArgumentException("BotDifficultyDef " + Difficulty + ": AimSpreadDegrees must be in [0, 180]");
            if (ReactionTicks < 0) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": ReactionTicks must be ≥ 0");
            RequireChance(UltUseChance, nameof(UltUseChance));
            RequireChance(UltMisuseChance, nameof(UltMisuseChance));
            RequireChance(TrickUseChance, nameof(TrickUseChance));
            RequireChance(TrickMisuseChance, nameof(TrickMisuseChance));
            RequireChance(EmptyBushShotChance, nameof(EmptyBushShotChance));
            RequireChance(RetreatThresholdJitter, nameof(RetreatThresholdJitter));
        }

        private void RequireChance(float value, string name)
        {
            if (value < 0f || value > 1f) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": " + name + " must be in [0, 1]");
        }
    }
}
