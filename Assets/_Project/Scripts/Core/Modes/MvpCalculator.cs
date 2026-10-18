using Arena.Core.Defs;
using Arena.Core.State;

namespace Arena.Core.Modes
{
    /// <summary>
    /// MVP of a finished match (results screen, GDD §5.3): the best score among the winning team (everyone on a draw);
    /// score = kills, damage, healing and acorns collected with the mode's weights, minus deaths. Ties go to the lower slot.
    /// Pure function over the state, called once at the end.
    /// </summary>
    public static class MvpCalculator
    {
        public static float Score(in FighterStats s, AcornsModeDef weights) =>
            s.Kills * weights.MvpKillWeight + s.DamageDealt * weights.MvpDamageWeight + s.HealingDone * weights.MvpHealWeight
            + s.AcornsCollected * weights.MvpAcornWeight - s.Deaths * weights.MvpDeathWeight;

        /// <summary>Slot of the MVP, or −1 if there is no candidate.</summary>
        public static int Compute(MatchState state, AcornsModeDef weights)
        {
            int winner = state.Mode.IsEnded ? state.Mode.WinnerTeam : AcornsRules.NoTeam;
            int best = -1;
            float bestScore = 0f;
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active) continue;
                if (winner != AcornsRules.NoTeam && f.Team != winner) continue;
                float score = Score(in state.Stats[i], weights);
                if (best >= 0 && score <= bestScore) continue;
                best = i;
                bestScore = score;
            }
            return best;
        }
    }
}
