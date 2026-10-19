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

        /// <summary>
        /// Lead on a moving target (F1, Normal): a projectile attack/ult aims at the target's position after the flight time
        /// times this fraction of its velocity (0 — at the target, 1 — full linear lead).
        /// </summary>
        public float LeadFraction;

        /// <summary>
        /// Focus on the weak (F1): the target is the perceived enemy with the lowest "distance + this × health fraction", in
        /// cells (0 — the nearest enemy).
        /// </summary>
        public float FocusHealthWeightCells;

        /// <summary>Chance per decision (0.2 s) that the bot sidesteps enemy projectiles until the next decision (0 — never).</summary>
        public float DodgeChance;

        /// <summary>A projectile is a threat when it reaches the bot within this many ticks.</summary>
        public int DodgeLookaheadTicks;

        /// <summary>…and passes closer than its radius + the bot's radius + this margin, cells.</summary>
        public float DodgeMarginCells;

        /// <summary>
        /// Ticks between two shots of the bot at least (D-131: the main lever of a weak bot — it shoots less often);
        /// the shared <c>BotBehaviourDef.MinShotIntervalTicks</c> applies when it is larger.
        /// </summary>
        public int ShotIntervalTicks;

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
            RequireChance(LeadFraction, nameof(LeadFraction));
            RequireChance(DodgeChance, nameof(DodgeChance));
            if (FocusHealthWeightCells < 0f) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": FocusHealthWeightCells must be ≥ 0");
            if (DodgeLookaheadTicks < 0) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": DodgeLookaheadTicks must be ≥ 0");
            if (DodgeMarginCells < 0f) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": DodgeMarginCells must be ≥ 0");
            if (ShotIntervalTicks < 0) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": ShotIntervalTicks must be ≥ 0");
        }

        private void RequireChance(float value, string name)
        {
            if (value < 0f || value > 1f) throw new ArgumentException("BotDifficultyDef " + Difficulty + ": " + name + " must be in [0, 1]");
        }
    }
}
