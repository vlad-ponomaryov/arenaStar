using System;

namespace Arena.Bots.Defs
{
    /// <summary>
    /// Behaviour parameters shared by all bot difficulties (GDD §7), converted from <c>BotBehaviourConfig</c>: ticks, cells,
    /// fractions and utility scores. Values not in the GDD are PROTO (Decisions.md).
    /// </summary>
    public sealed class BotBehaviourDef
    {
        /// <summary>Ticks between utility decisions (0.2 s = 6, GDD §7); between them the bot follows its path and aims.</summary>
        public int DecisionIntervalTicks;

        /// <summary>A bot perceives enemies (visible to its team) only within this radius, cells (≈ a phone screen, D-022).</summary>
        public float ViewRadius;

        /// <summary>Retreat below this fraction of max health (0.35, GDD §7) — strictly less.</summary>
        public float RetreatHealthFraction;

        /// <summary>A retreating bot keeps retreating until its health is back to this fraction (waits for regeneration).</summary>
        public float RecoverHealthFraction;

        /// <summary>The last charge is spent only if one attack finishes the target or the enemy is closer than this, cells.</summary>
        public float LastChargeEnemyDistance;

        /// <summary>Preferred distance to the target = attack range × this; farther away the bot closes in, nearer it strafes.</summary>
        public float EngageRangeFraction;

        /// <summary>Shoots when the target centre is within attack range × this.</summary>
        public float FireRangeFraction;

        /// <summary>Minimum ticks between two attack presses (a human does not empty all charges at once).</summary>
        public int MinShotIntervalTicks;

        /// <summary>Ticks a strafe direction is kept before a new random one is chosen.</summary>
        public int StrafeSwitchTicks;

        /// <summary>A path waypoint (cell centre) or the goal counts as reached within this distance, cells.</summary>
        public float WaypointReachDistance;

        /// <summary>Wander goals are random walkable cells within this many cells (per axis) of the bot.</summary>
        public int WanderRadiusCells;

        /// <summary>Random cells tried for a wander goal before standing still for this decision.</summary>
        public int WanderAttempts;

        // Utility scores (UtilityScorer): the highest possible action wins; ties go to the lower BotAction value.
        public float RetreatScore;
        public float AttackScore;

        /// <summary>Added to <see cref="AttackScore"/> × (1 − distance / view radius): nearer enemies are more urgent.</summary>
        public float AttackProximityBonus;
        public float ObjectiveScore;
        public float WanderScore;

        /// <summary>A* expands at most this many nodes per search (no path beyond it).</summary>
        public int PathNodeLimit;

        /// <summary>Waypoints kept per bot; a longer path is cut and re-planned when its end is reached.</summary>
        public int MaxPathLength;

        public void Validate()
        {
            if (DecisionIntervalTicks < 1) throw new ArgumentException("BotBehaviourDef.DecisionIntervalTicks must be ≥ 1");
            if (ViewRadius <= 0f) throw new ArgumentException("BotBehaviourDef.ViewRadius must be > 0");
            if (RetreatHealthFraction < 0f || RetreatHealthFraction > 1f)
                throw new ArgumentException("BotBehaviourDef.RetreatHealthFraction must be in [0, 1]");
            if (RecoverHealthFraction < RetreatHealthFraction || RecoverHealthFraction > 1f)
                throw new ArgumentException("BotBehaviourDef.RecoverHealthFraction must be in [RetreatHealthFraction, 1]");
            if (LastChargeEnemyDistance < 0f) throw new ArgumentException("BotBehaviourDef.LastChargeEnemyDistance must be ≥ 0");
            if (EngageRangeFraction <= 0f) throw new ArgumentException("BotBehaviourDef.EngageRangeFraction must be > 0");
            if (FireRangeFraction <= 0f) throw new ArgumentException("BotBehaviourDef.FireRangeFraction must be > 0");
            if (MinShotIntervalTicks < 0) throw new ArgumentException("BotBehaviourDef.MinShotIntervalTicks must be ≥ 0");
            if (StrafeSwitchTicks < 1) throw new ArgumentException("BotBehaviourDef.StrafeSwitchTicks must be ≥ 1");
            if (WaypointReachDistance <= 0f) throw new ArgumentException("BotBehaviourDef.WaypointReachDistance must be > 0");
            if (WanderRadiusCells < 1) throw new ArgumentException("BotBehaviourDef.WanderRadiusCells must be ≥ 1");
            if (WanderAttempts < 1) throw new ArgumentException("BotBehaviourDef.WanderAttempts must be ≥ 1");
            if (PathNodeLimit < 1) throw new ArgumentException("BotBehaviourDef.PathNodeLimit must be ≥ 1");
            if (MaxPathLength < 1) throw new ArgumentException("BotBehaviourDef.MaxPathLength must be ≥ 1");
        }
    }
}
