namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Cache of number, percent and clock strings for per-frame texts (CLAUDE.md §1: no allocations per frame): damage
    /// numbers, the own health number, the FPS counter, the HUD score and timer. Each string is created once on first use
    /// (or by <see cref="Prewarm"/>); values outside the cache fall back to a fresh string (never in the normal range).
    /// The capacities are technical limits (like <c>SimLimits</c>), not game parameters.
    /// </summary>
    public static class NumberStringCache
    {
        /// <summary>Cached integers 0..MaxCachedNumber (damage, health, acorns, seconds, statistics rows).</summary>
        public const int MaxCachedNumber = 9999;

        /// <summary>Cached percentages 0..MaxCachedPercent ("75%").</summary>
        public const int MaxCachedPercent = 100;

        /// <summary>Cached clock values 0:00..MaxCachedClockSeconds (a match lasts 2:30).</summary>
        public const int MaxCachedClockSeconds = 3599;

        private const int SecondsPerMinute = 60;
        private const int TwoDigits = 10;
        private const string PercentSign = "%";

        private static readonly string[] Numbers = new string[MaxCachedNumber + 1];
        private static readonly string[] Percents = new string[MaxCachedPercent + 1];
        private static readonly string[] Clocks = new string[MaxCachedClockSeconds + 1];

        public static string Get(int value)
        {
            if (value < 0 || value > MaxCachedNumber) return value.ToString();
            return Numbers[value] ?? (Numbers[value] = value.ToString());
        }

        /// <summary>"<paramref name="percent"/>%".</summary>
        public static string Percent(int percent)
        {
            if (percent < 0 || percent > MaxCachedPercent) return percent + PercentSign;
            return Percents[percent] ?? (Percents[percent] = percent + PercentSign);
        }

        /// <summary>"m:ss" for <paramref name="seconds"/> (negative = 0).</summary>
        public static string Clock(int seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds > MaxCachedClockSeconds) return Format(seconds);
            return Clocks[seconds] ?? (Clocks[seconds] = Format(seconds));
        }

        /// <summary>Creates the strings of 0..<paramref name="max"/> now (match start), so the first hits do not allocate.</summary>
        public static void Prewarm(int max)
        {
            if (max > MaxCachedNumber) max = MaxCachedNumber;
            for (int i = 0; i <= max; i++) Get(i);
        }

        private static string Format(int seconds)
        {
            int minutes = seconds / SecondsPerMinute;
            int rest = seconds % SecondsPerMinute;
            return minutes + (rest < TwoDigits ? ":0" : ":") + rest;
        }
    }
}
