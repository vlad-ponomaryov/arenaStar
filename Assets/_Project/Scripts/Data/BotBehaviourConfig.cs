using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Behaviour of the bots shared by all difficulties (GDD §7) in designer units: seconds, cells, fractions, utility scores.
    /// Converted to <c>Arena.Bots.Defs.BotBehaviourDef</c> by ConfigConverter. Values are written by DataSeeder (D-010).
    /// </summary>
    [CreateAssetMenu(fileName = "BotBehaviour", menuName = "Arena/Bot Behaviour")]
    public sealed class BotBehaviourConfig : ScriptableObject
    {
        [Header("Decisions and perception")]
        [Tooltip("Seconds between utility decisions (GDD §7: 0.2).")]
        [Min(0f)] public float DecisionIntervalSeconds;

        [Tooltip("Enemies are perceived only within this radius, cells (≈ a phone screen).")]
        [Min(0f)] public float ViewRadius;

        [Header("Retreat")]
        [Tooltip("Retreat below this fraction of max health (GDD §7: 0.35).")]
        [Range(0f, 1f)] public float RetreatHealthFraction;

        [Tooltip("Keep retreating until health is back to this fraction.")]
        [Range(0f, 1f)] public float RecoverHealthFraction;

        [Header("Fighting")]
        [Tooltip("The last charge is spent only to finish the target or if the enemy is closer than this, cells.")]
        [Min(0f)] public float LastChargeEnemyDistance;

        [Tooltip("Preferred distance to the target as a fraction of the attack range (closer in = strafe).")]
        [Min(0f)] public float EngageRangeFraction;

        [Tooltip("Shoot when the target is within this fraction of the attack range.")]
        [Min(0f)] public float FireRangeFraction;

        [Tooltip("Minimum seconds between two attack presses.")]
        [Min(0f)] public float MinShotIntervalSeconds;

        [Tooltip("Seconds a strafe direction is kept.")]
        [Min(0f)] public float StrafeSwitchSeconds;

        [Header("Movement")]
        [Tooltip("A waypoint or goal counts as reached within this distance, cells.")]
        [Min(0f)] public float WaypointReachDistance;

        [Tooltip("Wander goals: random walkable cells within this many cells of the bot.")]
        [Min(1)] public int WanderRadiusCells;

        [Min(1)] public int WanderAttempts;

        [Header("Utility scores")]
        public float RetreatScore;
        public float AttackScore;

        [Tooltip("Added to the attack score × (1 − distance / view radius).")]
        public float AttackProximityBonus;
        public float ObjectiveScore;
        public float WanderScore;

        [Header("Pathfinding")]
        [Tooltip("A* expands at most this many nodes per search.")]
        [Min(1)] public int PathNodeLimit;

        [Tooltip("Waypoints kept per bot; longer paths are re-planned when their end is reached.")]
        [Min(1)] public int MaxPathLength;

        [Header("Ult and trick")]
        [Tooltip("The ult is worth it with this many enemies in its zone (GDD §7: 2).")]
        [Min(1)] public int UltMinEnemies;

        [Tooltip("'Focused' (helmet trick): damaged by this many different enemies within the window (GDD §7: 2).")]
        [Min(1)] public int FocusMinEnemies;

        [Min(0f)] public float FocusWindowSeconds;

        [Tooltip("Escape trick: retreating with an enemy within this distance, cells.")]
        [Min(0f)] public float EscapeEnemyDistance;

        [Tooltip("'Enemies close' trick: an enemy within this distance, cells.")]
        [Min(0f)] public float EnemyCloseDistance;

        [Tooltip("Heal trick: the bot or an ally in range is below this fraction of health.")]
        [Range(0f, 1f)] public float HealAllyHealthFraction;

        [Tooltip("Objective trick (mine): within this distance of the objective, cells.")]
        [Min(0f)] public float ObjectivePlaceDistance;

        [Header("Memory and bushes")]
        [Tooltip("A lost enemy is remembered this long (bush checks).")]
        [Min(0f)] public float MemorySeconds;

        [Tooltip("The remembered position is extrapolated by the last velocity for at most this long.")]
        [Min(0f)] public float MemoryPredictSeconds;

        [Tooltip("A bush within this many cells of the remembered point is checked.")]
        [Min(0)] public int BushNearCells;

        [Tooltip("A retreating bot hides in a bush within this many cells.")]
        [Min(0)] public int HideBushRadiusCells;

        [Tooltip("Random cells tried for an empty-bush shot (a mistake).")]
        [Min(1)] public int EmptyBushShotAttempts;

        [Header("Acorns carrier")]
        [Tooltip("A bot carrying this many acorns plays carefully (GDD §7: 8).")]
        [Min(1)] public int CarrierAcorns;

        [Range(0f, 1f)] public float CarrierRetreatHealthFraction;

        [Tooltip("The carrier keeps this far behind the allies (towards the own spawn), cells.")]
        [Min(0f)] public float CarrierBehindDistance;

        [Header("Utility scores (S2.9)")]
        public float HideInBushScore;
        public float CheckBushScore;
        public float StayWithTeamScore;

        [Header("Last Beast (S3.3)")]
        [Tooltip("Score of fleeing the fog (above retreat and hiding).")]
        public float FleeFogScore;

        [Tooltip("Flee when the next fog step within … seconds would cover the bot.")]
        [Min(0f)] public float FogWarnSeconds;

        [Header("Last Beast pace (S3.14)")]
        [Tooltip("Score of breaking a berry box (below the berry objective and the attack, above wandering).")]
        public float BreakBoxScore;

        [Tooltip("Berries and boxes within … cells (per axis) are loot targets; 0 — no boxes, berries at any distance.")]
        [Min(0)] public int LootRadiusCells;

        [Tooltip("Fleeing the fog: own position clamped into the next safe square shrunk by … cells.")]
        [Min(0f)] public float FogSafeMarginCells;

        [Tooltip("Last Beast wandering: random safe cells within … cells (per axis).")]
        [Min(0)] public int ExploreRadiusCells;

        [Tooltip("While more than CautiousUntilAlive are alive, enemies farther than … cells are not engaged unless they hit the bot recently; 0 — off.")]
        [Min(0f)] public float CautiousEngageDistance;

        [Min(0)] public int CautiousUntilAlive;

        [Tooltip("An enemy that hit the bot within … seconds is engaged at any distance.")]
        [Min(0f)] public float ProvokedSeconds;
    }
}
