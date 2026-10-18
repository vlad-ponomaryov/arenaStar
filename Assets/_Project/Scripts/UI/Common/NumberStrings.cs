namespace Arena.UI.Common
{
    /// <summary>
    /// Cache of number and clock strings for per-frame HUD texts (CLAUDE.md §1: no allocations per frame). Each string is
    /// created once on first use; values outside the cache fall back to a fresh string (never in the normal HUD range).
    /// The capacities are technical limits (like <c>SimLimits</c>), not game parameters.
    /// </summary>
    public static class NumberStrings
    {
        /// <summary>Cached integers 0..MaxCachedNumber (acorns, seconds, statistics rows).</summary>
        public const int MaxCachedNumber = 999;

        /// <summary>Cached clock values 0:00..MaxCachedClockSeconds (a match lasts 2:30).</summary>
        public const int MaxCachedClockSeconds = 3599;

        private const int SecondsPerMinute = 60;
        private const int TwoDigits = 10;

        private static readonly string[] Numbers = new string[MaxCachedNumber + 1];
        private static readonly string[] Clocks = new string[MaxCachedClockSeconds + 1];

        public static string Get(int value)
        {
            if (value < 0 || value > MaxCachedNumber) return value.ToString();
            return Numbers[value] ?? (Numbers[value] = value.ToString());
        }

        /// <summary>"m:ss" for <paramref name="seconds"/> (negative = 0).</summary>
        public static string Clock(int seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds > MaxCachedClockSeconds) return Format(seconds);
            return Clocks[seconds] ?? (Clocks[seconds] = Format(seconds));
        }

        private static string Format(int seconds)
        {
            int minutes = seconds / SecondsPerMinute;
            int rest = seconds % SecondsPerMinute;
            return minutes + (rest < TwoDigits ? ":0" : ":") + rest;
        }
    }
}
