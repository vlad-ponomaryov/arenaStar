namespace Arena.Bots.Utility
{
    /// <summary>What a bot knows when deciding about its trick (input of <see cref="TrickDecision"/>), filled by <c>BotController</c>.</summary>
    public struct TrickSituation
    {
        /// <summary>Different enemies that damaged the bot within <c>FocusWindowTicks</c>.</summary>
        public int RecentAttackers;

        /// <summary>The bot wants to retreat (low health).</summary>
        public bool Retreating;

        /// <summary>A perceived enemy (the decision's target) and its distance, cells.</summary>
        public bool HasTarget;
        public float TargetDistance;

        /// <summary>The target is within the trick reach plus the attack range (<c>BotTrickHint.Engage</c>).</summary>
        public bool TargetInEngageReach;

        /// <summary>Lowest health fraction among the bot and its allies within the trick range.</summary>
        public float LowestAllyHealthFraction;

        /// <summary>Distance to the mode objective, cells (+∞ without one).</summary>
        public float ObjectiveDistance;
    }
}
