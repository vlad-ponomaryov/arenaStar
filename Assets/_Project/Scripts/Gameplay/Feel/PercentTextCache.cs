namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Texts of a format with one percent value ("+{0}% power"), built once per value and kept (D-131): no allocation per frame
    /// once a value was shown. Values outside [0, <see cref="Capacity"/>) are formatted each time (never in practice).
    /// </summary>
    public sealed class PercentTextCache
    {
        public const int Capacity = 1024;

        private readonly string _format;
        private readonly string[] _texts = new string[Capacity];

        public PercentTextCache(string format)
        {
            _format = format;
        }

        public string Get(int percent)
        {
            if (percent < 0 || percent >= Capacity) return string.Format(_format, percent);
            return _texts[percent] ?? (_texts[percent] = string.Format(_format, percent));
        }
    }
}
