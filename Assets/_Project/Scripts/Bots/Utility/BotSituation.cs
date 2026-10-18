namespace Arena.Bots.Utility
{
    /// <summary>What a bot knows at a decision (input of <see cref="UtilityScorer"/>), filled by <c>BotController</c>.</summary>
    public struct BotSituation
    {
        /// <summary>Health / max health, 0..1.</summary>
        public float HealthFraction;

        /// <summary>The bot is retreating already (hysteresis: it retreats until <c>RecoverHealthFraction</c>).</summary>
        public bool Retreating;

        /// <summary>An enemy is perceived (visible to the bot's team and within the view radius).</summary>
        public bool HasTarget;

        /// <summary>Distance to that enemy, cells.</summary>
        public float TargetDistance;

        /// <summary>The mode has an objective point (acorn, hollow, map centre).</summary>
        public bool HasObjective;
    }
}
