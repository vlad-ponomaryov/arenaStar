using System;

namespace Arena.Net.Client
{
    /// <summary>
    /// Automatic return to a running match after a lost connection (Server-Spec §7, §13 "C": <c>ReconnectRetrySeconds</c>,
    /// <c>ReconnectWindowSeconds</c>; S3.12). The game reads them from <c>NetConfig</c>, tests use <see cref="CreateDefault"/> —
    /// the single place in code for these PROTO numbers (the window is GDD §8.3).
    /// </summary>
    public sealed class ReconnectSettings
    {
        /// <summary>
        /// Pause before attempt k (after the connection was lost or the previous attempt failed), seconds; the last value repeats.
        /// An attempt is one handshake of the transport (up to its <c>ConnectTimeoutMs</c>).
        /// </summary>
        public float[] RetrySeconds;

        /// <summary>Attempts stop this long after the last packet from the server (GDD §8.3: 60 s).</summary>
        public float WindowSeconds;

        public static ReconnectSettings CreateDefault() => new ReconnectSettings
        {
            RetrySeconds = new[] { 0.5f, 1f, 2f, 3f, 5f },
            WindowSeconds = 60f
        };

        /// <summary>Pause before attempt <paramref name="attempt"/> (0-based), seconds.</summary>
        public float DelayBefore(int attempt)
        {
            if (attempt < 0) attempt = 0;
            return RetrySeconds[attempt < RetrySeconds.Length ? attempt : RetrySeconds.Length - 1];
        }

        public void Validate()
        {
            if (RetrySeconds == null || RetrySeconds.Length == 0) throw new ArgumentException("ReconnectSettings: no retry delays");
            for (int i = 0; i < RetrySeconds.Length; i++)
                if (RetrySeconds[i] < 0f) throw new ArgumentException("ReconnectSettings: a retry delay is negative");
            if (WindowSeconds < 0f) throw new ArgumentException("ReconnectSettings: the window is negative");
        }
    }
}
