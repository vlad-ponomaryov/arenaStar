using System;

namespace Arena.Net.Server.Match
{
    /// <summary>Why a bot drives a slot (Server-Spec §7); several can hold at once — the bot leaves when none is left.</summary>
    [Flags]
    public enum BotControlReason : byte
    {
        None = 0,

        /// <summary>No player in this slot at all (a fill bot, the whole match).</summary>
        Fill = 1 << 0,

        /// <summary>The player is not connected (did not come, timed out, closed).</summary>
        Disconnected = 1 << 1,

        /// <summary>No input for <c>BotTakeoverSeconds</c>.</summary>
        NoInput = 1 << 2,

        /// <summary>Only zero inputs for <c>AfkSeconds</c>.</summary>
        Afk = 1 << 3,

        /// <summary>The player sent <c>Leave</c>.</summary>
        Left = 1 << 4,
    }
}
