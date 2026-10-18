namespace Arena.Core.Defs
{
    /// <summary>
    /// When a bot should use this fighter's trick (GDD §7 "uses the trick by the fighter's rules"). Data of the fighter
    /// (<see cref="FighterDef.BotTrickHint"/>), read only by the bots; the simulation ignores it. Append new values at the
    /// end — the value is serialized in configs.
    /// </summary>
    public enum BotTrickHint : byte
    {
        /// <summary>Bots never use the trick on purpose.</summary>
        None = 0,

        /// <summary>Taking damage from 2+ enemies within a short window (Bruno's helmet).</summary>
        WhenFocused = 1,

        /// <summary>Retreating with an enemy nearby: away from it (Pix's roll).</summary>
        Escape = 2,

        /// <summary>A healthy bot with its target within the trick reach plus the attack range: towards it.</summary>
        Engage = 3,

        /// <summary>The bot or an ally within the trick range is hurt (heals).</summary>
        HealAllies = 4,

        /// <summary>An enemy is very close (push-back, smoke).</summary>
        WhenEnemiesClose = 5,

        /// <summary>Standing at the mode objective (mine at the hollow).</summary>
        PlaceAtObjective = 6
    }
}
