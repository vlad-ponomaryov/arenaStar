using System;

namespace Arena.Net.Client
{
    /// <summary>
    /// Client lobby parameters (Server-Spec §6.3, §13 "C" — <c>NetConfig</c> from S3.10, tests set them in code). Values of
    /// <see cref="CreateDefault"/> are PROTO (§13), the single place in code for these numbers. Times are milliseconds.
    /// </summary>
    public sealed class LobbyClientSettings
    {
        /// <summary>Pings per region; the median decides (§6.3, §13 <c>PingSamples</c>).</summary>
        public int PingSamples;

        /// <summary>Pause between the pings of one region (all regions are pinged in parallel).</summary>
        public int PingIntervalMs;

        /// <summary>A ping without a pong this long after the last one was sent is lost.</summary>
        public int PingTimeoutMs;

        /// <summary>Below this the ping badge is green (GDD §8.2).</summary>
        public int PingGoodMs;

        /// <summary>Below this — yellow, from it — red (GDD §8.2: red from 250 ms).</summary>
        public int PingFairMs;

        /// <summary>LAN <c>Discover</c> broadcasts per search (UDP may drop one).</summary>
        public int DiscoverRepeats;

        public int DiscoverIntervalMs;

        /// <summary>The search ends this long after the last broadcast.</summary>
        public int DiscoverTimeoutMs;

        public static LobbyClientSettings CreateDefault() => new LobbyClientSettings
        {
            PingSamples = 5,
            PingIntervalMs = 100,
            PingTimeoutMs = 1000,
            PingGoodMs = 100,
            PingFairMs = 250,
            DiscoverRepeats = 3,
            DiscoverIntervalMs = 200,
            DiscoverTimeoutMs = 600,
        };

        public void Validate()
        {
            if (PingSamples < 1 || PingSamples > NetLimits.MaxPingSamples)
                throw new ArgumentException("LobbyClientSettings: PingSamples must be in [1, MaxPingSamples]");
            if (PingIntervalMs < 0 || PingTimeoutMs <= 0) throw new ArgumentException("LobbyClientSettings: ping times");
            if (PingGoodMs <= 0 || PingFairMs < PingGoodMs) throw new ArgumentException("LobbyClientSettings: ping thresholds");
            if (DiscoverRepeats < 1 || DiscoverIntervalMs < 0 || DiscoverTimeoutMs <= 0)
                throw new ArgumentException("LobbyClientSettings: discovery times");
        }

        public LobbyClientSettings Clone() => (LobbyClientSettings)MemberwiseClone();
    }
}
