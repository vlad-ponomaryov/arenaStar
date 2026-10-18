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
    }
}
