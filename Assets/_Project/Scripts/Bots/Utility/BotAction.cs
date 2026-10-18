namespace Arena.Bots.Utility
{
    /// <summary>What a bot is doing between two decisions (utility AI, GDD §7). S2.9 adds ult/trick, bushes and acorn tactics.</summary>
    public enum BotAction : byte
    {
        None = 0,

        /// <summary>Health below the threshold: go back to the nearest own spawn and wait for regeneration.</summary>
        Retreat = 1,

        /// <summary>A perceived enemy: close in to the preferred distance, strafe, shoot (charges, reaction, spread).</summary>
        Attack = 2,

        /// <summary>The mode objective: nearest acorn, otherwise the hollow (map centre without a mode).</summary>
        MoveToObjective = 3,

        /// <summary>Nothing better to do: walk to a random nearby cell.</summary>
        Wander = 4
    }
}
