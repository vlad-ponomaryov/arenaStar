using System;
using Arena.Net.Client;

namespace Arena.Data.Conversion
{
    /// <summary>
    /// <see cref="NetConfig"/> → the plain settings of <c>Arena.Net</c> (Server-Spec §13 "C" rows). The game client answers
    /// <c>MatchStart</c> only after the Match scene has loaded, so <see cref="ClientMatchSettings.ReadyOnMatchStart"/> is false.
    /// </summary>
    public static class NetConfigConverter
    {
        public static ClientMatchSettings ToClientMatchSettings(NetConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            return new ClientMatchSettings
            {
                TargetInputMarginTicks = c.TargetInputMarginTicks,
                MarginGain = c.MarginGain,
                MaxTimeDilation = c.MaxTimeDilation,
                HardResyncTicks = c.HardResyncTicks,
                MarginSettleTicks = c.MarginSettleTicks,
                MaxResimTicks = c.MaxResimTicks,
                MaxTicksPerFrame = c.MaxTicksPerFrame,
                FallbackRttMs = c.FallbackRttMs,
                CorrectionSmoothingSeconds = c.CorrectionSmoothingSeconds,
                SnapDistanceCells = c.SnapDistanceCells,
                LargeCorrectionCells = c.LargeCorrectionCells,
                InterpDelaySnapshots = c.InterpDelaySnapshots,
                InterpJitterFactor = c.InterpJitterFactor,
                InterpJitterSmoothing = c.InterpJitterSmoothing,
                InterpGain = c.InterpGain,
                InterpDelayMaxMs = c.InterpDelayMaxMs,
                MaxExtrapolationMs = c.MaxExtrapolationMs,
                ReadyOnMatchStart = false
            };
        }

        public static LobbyClientSettings ToLobbySettings(NetConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var s = new LobbyClientSettings
            {
                PingSamples = c.PingSamples,
                PingIntervalMs = c.PingIntervalMs,
                PingTimeoutMs = c.PingTimeoutMs,
                PingGoodMs = c.PingGoodMs,
                PingFairMs = c.PingFairMs,
                DiscoverRepeats = c.DiscoverRepeats,
                DiscoverIntervalMs = c.DiscoverIntervalMs,
                DiscoverTimeoutMs = c.DiscoverTimeoutMs
            };
            s.Validate();
            return s;
        }
    }
}
