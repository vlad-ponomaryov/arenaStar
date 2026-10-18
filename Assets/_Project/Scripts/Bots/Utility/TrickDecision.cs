using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Random;

namespace Arena.Bots.Utility
{
    /// <summary>
    /// When a bot uses its trick (GDD §7 "by the fighter's rules"): the fighter's <see cref="BotTrickHint"/> names the
    /// situation (helmet when focused by 2+ enemies, roll to escape, …); when it fits, the trick is used with
    /// <see cref="BotDifficultyDef.TrickUseChance"/> per decision, otherwise with an enemy around it is wasted with
    /// <see cref="BotDifficultyDef.TrickMisuseChance"/>. The caller checks that the trick is ready. Pure functions.
    /// </summary>
    public static class TrickDecision
    {
        /// <summary>True if the situation is the one the hint describes.</summary>
        public static bool Fits(BotTrickHint hint, in TrickSituation s, BotBehaviourDef b)
        {
            switch (hint)
            {
                case BotTrickHint.WhenFocused:
                    return s.RecentAttackers >= b.FocusMinEnemies;
                case BotTrickHint.Escape:
                    return s.Retreating && s.HasTarget && s.TargetDistance <= b.EscapeEnemyDistance;
                case BotTrickHint.Engage:
                    return !s.Retreating && s.HasTarget && s.TargetInEngageReach;
                case BotTrickHint.HealAllies:
                    return s.LowestAllyHealthFraction < b.HealAllyHealthFraction;
                case BotTrickHint.WhenEnemiesClose:
                    return s.HasTarget && s.TargetDistance <= b.EnemyCloseDistance;
                case BotTrickHint.PlaceAtObjective:
                    return s.ObjectiveDistance <= b.ObjectivePlaceDistance;
                default:
                    return false;
            }
        }

        /// <summary>The per-decision roll of the difficulty (see the class summary).</summary>
        public static bool Roll(BotTrickHint hint, in TrickSituation s, BotBehaviourDef b, BotDifficultyDef d, ref DeterministicRng rng)
        {
            if (hint == BotTrickHint.None) return false;
            if (Fits(hint, in s, b)) return rng.NextFloat01() < d.TrickUseChance;
            return s.HasTarget && d.TrickMisuseChance > 0f && rng.NextFloat01() < d.TrickMisuseChance;
        }
    }
}
