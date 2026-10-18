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

        // --- Ult and trick (S2.9). ---

        /// <summary>The ult is worth it with this many perceived enemies in its zone (GDD §7: 2).</summary>
        public int UltMinEnemies;

        /// <summary>"Focused" (<c>BotTrickHint.WhenFocused</c>): damaged by this many different enemies within <see cref="FocusWindowTicks"/>.</summary>
        public int FocusMinEnemies;
        public int FocusWindowTicks;

        /// <summary><c>BotTrickHint.Escape</c>: retreating with a perceived enemy within this distance, cells.</summary>
        public float EscapeEnemyDistance;

        /// <summary><c>BotTrickHint.WhenEnemiesClose</c>: a perceived enemy within this distance, cells.</summary>
        public float EnemyCloseDistance;

        /// <summary><c>BotTrickHint.HealAllies</c>: the bot or an ally in the trick range is below this health fraction.</summary>
        public float HealAllyHealthFraction;

        /// <summary><c>BotTrickHint.PlaceAtObjective</c>: within this distance of the objective, cells.</summary>
        public float ObjectivePlaceDistance;

        // --- Memory and bushes (S2.9). ---

        /// <summary>An enemy lost from sight is remembered (last position, velocity, tick) this long.</summary>
        public int MemoryTicks;

        /// <summary>The remembered position is extrapolated by the last velocity for at most this many ticks.</summary>
        public int MemoryPredictTicks;

        /// <summary>A bush cell within this many cells (per axis) of the extrapolated point is checked by an attack.</summary>
        public int BushNearCells;

        /// <summary>A retreating bot hides in a bush within this many cells (per axis) instead of walking to its spawn.</summary>
        public int HideBushRadiusCells;

        /// <summary>Random cells tried to find a bush for an empty-bush shot (a mistake).</summary>
        public int EmptyBushShotAttempts;

        // --- Acorns carrier (S2.9). ---

        /// <summary>A bot carrying at least this many acorns plays carefully and stays with its team (GDD §7: 8).</summary>
        public int CarrierAcorns;

        /// <summary>The carrier retreats below this health fraction (if higher than its normal threshold).</summary>
        public float CarrierRetreatHealthFraction;

        /// <summary>The carrier's spot: the allies' centre moved this far towards the own spawn, cells.</summary>
        public float CarrierBehindDistance;

        public float HideInBushScore;
        public float CheckBushScore;
        public float StayWithTeamScore;

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
            if (UltMinEnemies < 1) throw new ArgumentException("BotBehaviourDef.UltMinEnemies must be ≥ 1");
            if (FocusMinEnemies < 1) throw new ArgumentException("BotBehaviourDef.FocusMinEnemies must be ≥ 1");
            if (FocusWindowTicks < 1) throw new ArgumentException("BotBehaviourDef.FocusWindowTicks must be ≥ 1");
            if (EscapeEnemyDistance < 0f || EnemyCloseDistance < 0f || ObjectivePlaceDistance < 0f)
                throw new ArgumentException("BotBehaviourDef: trick distances must be ≥ 0");
            if (HealAllyHealthFraction < 0f || HealAllyHealthFraction > 1f)
                throw new ArgumentException("BotBehaviourDef.HealAllyHealthFraction must be in [0, 1]");
            if (MemoryTicks < 0 || MemoryPredictTicks < 0) throw new ArgumentException("BotBehaviourDef: memory ticks must be ≥ 0");
            if (BushNearCells < 0 || HideBushRadiusCells < 0) throw new ArgumentException("BotBehaviourDef: bush radii must be ≥ 0");
            if (EmptyBushShotAttempts < 1) throw new ArgumentException("BotBehaviourDef.EmptyBushShotAttempts must be ≥ 1");
            if (CarrierAcorns < 1) throw new ArgumentException("BotBehaviourDef.CarrierAcorns must be ≥ 1");
            if (CarrierRetreatHealthFraction < 0f || CarrierRetreatHealthFraction > 1f)
                throw new ArgumentException("BotBehaviourDef.CarrierRetreatHealthFraction must be in [0, 1]");
            if (CarrierBehindDistance < 0f) throw new ArgumentException("BotBehaviourDef.CarrierBehindDistance must be ≥ 0");
        }
    }
}
