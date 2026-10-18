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

        /// <summary>The perceived enemy is within the bot's fire range (a careful carrier attacks only then).</summary>
        public bool TargetInFireRange;

        /// <summary>Random shift of the retreat threshold for this life (difficulty "mistake": sometimes too late).</summary>
        public float RetreatShift;

        /// <summary>Carries <c>CarrierAcorns</c>+ acorns: retreats earlier, stays with the team, does not chase or check bushes.</summary>
        public bool IsCarrier;

        /// <summary>A bush to hide in was found (only searched when the bot wants to retreat).</summary>
        public bool HasHideBush;

        /// <summary>An enemy vanished near a bush within the memory time.</summary>
        public bool HasBushMemory;

        /// <summary>Last Beast: in the fog now, or covered by the next fog step that comes within <c>FogWarnTicks</c>.</summary>
        public bool FogThreat;
    }
}
