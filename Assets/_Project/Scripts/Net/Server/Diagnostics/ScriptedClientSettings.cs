namespace Arena.Net.Server.Diagnostics
{
    /// <summary>
    /// Clock of a <see cref="ScriptedMatchClient"/> (test tool; the game client's clock settings live in <c>NetConfig</c>, S3.7a).
    /// Values of <see cref="CreateDefault"/> mirror Server-Spec §13: target margin 1 tick, adjustments once per margin window.
    /// </summary>
    public sealed class ScriptedClientSettings
    {
        /// <summary>The first input tick = first snapshot tick + this (≈ RTT in ticks + margin for 150 ms).</summary>
        public int InitialLeadTicks;

        /// <summary>The client keeps the server's <c>InputMarginMin</c> at this.</summary>
        public int TargetMarginTicks;

        /// <summary>A margin up to target + this is left alone; above it the client holds its tick.</summary>
        public int MarginSlackTicks;

        /// <summary>At most one adjustment per this many client ticks (the server's margin window).</summary>
        public int AdjustIntervalTicks;

        public static ScriptedClientSettings CreateDefault() => new ScriptedClientSettings
        {
            InitialLeadTicks = 6,
            TargetMarginTicks = 1,
            MarginSlackTicks = 3,
            AdjustIntervalTicks = 30
        };
    }
}
