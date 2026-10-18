using Arena.Bots.Defs;
using Arena.Core.Math;

namespace Arena.Bots.Utility
{
    /// <summary>
    /// Utility AI of the bots (GDD §7): every action gets a score from the situation and the data; the highest wins, ties go to
    /// the lower <see cref="BotAction"/> value (Retreat, Attack, MoveToObjective, Wander). Pure functions, no state.
    /// </summary>
    public static class UtilityScorer
    {
        /// <summary>Score of <paramref name="action"/>; 0 when it is not possible now.</summary>
        public static float Score(BotAction action, in BotSituation s, BotBehaviourDef b)
        {
            switch (action)
            {
                case BotAction.Retreat:
                    bool low = s.HealthFraction < b.RetreatHealthFraction || (s.Retreating && s.HealthFraction < b.RecoverHealthFraction);
                    return low ? b.RetreatScore : 0f;
                case BotAction.Attack:
                    if (!s.HasTarget) return 0f;
                    return b.AttackScore + b.AttackProximityBonus * (1f - MathUtil.Clamp01(s.TargetDistance / b.ViewRadius));
                case BotAction.MoveToObjective:
                    return s.HasObjective ? b.ObjectiveScore : 0f;
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
            for (BotAction a = BotAction.Retreat; a <= BotAction.Wander; a++)
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
