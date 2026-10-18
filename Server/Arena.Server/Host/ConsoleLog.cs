using System;
using System.Globalization;
using System.IO;

namespace Arena.Server.Host
{
    /// <summary>
    /// Log lines to stdout (Server-Spec §5.6, convenient for Docker and systemd): <c>2026-10-18T20:51:02.123Z INFO  text</c>, UTC,
    /// invariant culture, lines at or above <see cref="Level"/>. Thread-safe (the game and the metrics thread both log). Errors of
    /// a match carry its <c>matchId</c>; IP addresses are never written (§6.4), connections are named by index only.
    /// </summary>
    public sealed class ConsoleLog
    {
        private readonly TextWriter _out;
        private readonly object _lock = new object();

        public ConsoleLog(TextWriter output, LogLevel level)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
            Level = level;
        }

        public LogLevel Level { get; set; }

        public long Errors { get; private set; }

        public long Warnings { get; private set; }

        public bool IsEnabled(LogLevel level) => level >= Level;

        public void Debug(string message) => Write(LogLevel.Debug, message);

        public void Info(string message) => Write(LogLevel.Info, message);

        public void Warn(string message) => Write(LogLevel.Warn, message);

        public void Error(string message) => Write(LogLevel.Error, message);

        public void Error(ulong matchId, string message) => Write(LogLevel.Error, "match " + FormatMatchId(matchId) + ": " + message);

        public static string FormatMatchId(ulong matchId) => matchId.ToString("x16", CultureInfo.InvariantCulture);

        public void Write(LogLevel level, string message)
        {
            lock (_lock)
            {
                if (level == LogLevel.Error) Errors++;
                else if (level == LogLevel.Warn) Warnings++;
                if (level < Level) return;
                _out.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + " " + Tag(level) + " " + message);
                _out.Flush();
            }
        }

        private static string Tag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warn: return "WARN ";
                default: return "ERROR";
            }
        }
    }
}
