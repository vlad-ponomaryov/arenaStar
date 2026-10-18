using Arena.Bots.Defs;
using Arena.Core.Math;

namespace Arena.Bots.Utility
{
    /// <summary>
    /// Utility AI of the bots (GDD §7): every action gets a score from the situation and the data; the highest wins, ties go to
    /// the lower <see cref="BotAction"/> value. Pure functions, no state.
    /// Low health: <see cref="BotAction.HideInBush"/> if a bush was found, else <see cref="BotAction.Retreat"/>; the threshold is
    /// shifted per life (<see cref="BotSituation.RetreatShift"/>) and raised for an acorns carrier. A carrier attacks only an enemy
    /// in fire range, does not check bushes and stays with its team instead of going to the objective. In Last Beast the fog
    /// threat (<see cref="BotAction.FleeFog"/>) scores above everything (data).
    /// </summary>
    public static class UtilityScorer
    {
        public const BotAction FirstAction = BotAction.Retreat;
        public const BotAction LastAction = BotAction.FleeFog;

        /// <summary>Retreat threshold of this bot now: base + shift, raised for a carrier.</summary>
        public static float RetreatThreshold(in BotSituation s, BotBehaviourDef b)
        {
            float threshold = MathUtil.Clamp01(b.RetreatHealthFraction + s.RetreatShift);
            if (s.IsCarrier && b.CarrierRetreatHealthFraction > threshold) threshold = b.CarrierRetreatHealthFraction;
            return threshold;
        }

        /// <summary>True if the bot wants to get away and regenerate (below the threshold, or retreating and not recovered yet).</summary>
        public static bool WantsRetreat(in BotSituation s, BotBehaviourDef b)
        {
            float threshold = RetreatThreshold(in s, b);
            float recover = MathUtil.Max(b.RecoverHealthFraction, threshold);
            return s.HealthFraction < threshold || (s.Retreating && s.HealthFraction < recover);
        }

        /// <summary>Score of <paramref name="action"/>; 0 when it is not possible now.</summary>
        public static float Score(BotAction action, in BotSituation s, BotBehaviourDef b)
        {
            switch (action)
            {
                case BotAction.Retreat:
                    return WantsRetreat(in s, b) ? b.RetreatScore : 0f;
                case BotAction.HideInBush:
                    return s.HasHideBush && WantsRetreat(in s, b) ? b.HideInBushScore : 0f;
                case BotAction.Attack:
                    if (!s.HasTarget || (s.IsCarrier && !s.TargetInFireRange)) return 0f;
                    return b.AttackScore + b.AttackProximityBonus * (1f - MathUtil.Clamp01(s.TargetDistance / b.ViewRadius));
                case BotAction.CheckBush:
                    return s.HasBushMemory && !s.IsCarrier ? b.CheckBushScore : 0f;
                case BotAction.StayWithTeam:
                    return s.IsCarrier ? b.StayWithTeamScore : 0f;
                case BotAction.MoveToObjective:
                    return s.HasObjective && !s.IsCarrier ? b.ObjectiveScore : 0f;
                case BotAction.FleeFog:
                    return s.FogThreat ? b.FleeFogScore : 0f;
                case BotAction.Wander:
                    return b.WanderScore;
                default:
                    return 0f;
            }
        }

        /// <summary>The best action for the situation.</summary>
        public static BotAction Choose(in BotSituation s, BotBehaviourDef b)
        {
            BotAction best = BotAction.Wander;
            float bestScore = float.NegativeInfinity;
            for (BotAction a = FirstAction; a <= LastAction; a++)
            {
                float score = Score(a, in s, b);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }
            return best;
        }
    }
}
