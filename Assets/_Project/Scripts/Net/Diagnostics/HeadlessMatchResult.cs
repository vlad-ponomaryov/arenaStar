namespace Arena.Net.Diagnostics
{
    /// <summary>Outcome of a <see cref="HeadlessMatch"/> run: how long it went, who won, the final <see cref="StateHash"/>.</summary>
    public struct HeadlessMatchResult
    {
        public int Ticks;
        public bool Ended;

        /// <summary><c>ModeState.WinnerTeam</c> (−1 = draw) when <see cref="Ended"/>.</summary>
        public int WinnerTeam;

        public ulong StateHash;

        /// <summary>Total damage of all slots (a sanity check that the bots fought).</summary>
        public int TotalDamage;

        /// <summary>Average simulation + bots time per tick, milliseconds.</summary>
        public double AverageTickMs;

        /// <summary>Slowest tick, milliseconds.</summary>
        public double MaxTickMs;
    }
}
