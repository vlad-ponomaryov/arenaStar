namespace Arena.Bots.Utility
{
    /// <summary>
    /// What a bot is doing between two decisions (utility AI, GDD §7). Ult and trick are not actions: they are pressed on top
    /// of any action (<c>UltDecision</c>, <c>TrickDecision</c>). Append new values at the end (ties go to the lower value).
    /// </summary>
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
        Wander = 4,

        /// <summary>Health below the threshold and a bush nearby (not towards the threat): hide there and wait for regeneration.</summary>
        HideInBush = 5,

        /// <summary>An enemy vanished near a bush a moment ago: go within range and check the bush with an attack.</summary>
        CheckBush = 6,

        /// <summary>Acorns carrier (8+): keep just behind the allies instead of running to the objective.</summary>
        StayWithTeam = 7,

        /// <summary>Last Beast: the bot is in the fog, or the next fog step comes soon and will cover it — run to the safe centre.</summary>
        FleeFog = 8,

        /// <summary>Last Beast: nothing better around — go next to the nearest berry box (not in the fog soon) and shoot it.</summary>
        BreakBox = 9
    }
}
