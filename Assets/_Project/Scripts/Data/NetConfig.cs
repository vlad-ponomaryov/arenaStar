using System.Collections.Generic;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Client network settings (Server-Spec §12.3, §13 — the "C" rows): regions, the content pack shared with the server
    /// (D-083), the prediction / interpolation parameters (<c>ClientMatchSettings</c>), the lobby parameters
    /// (<c>LobbyClientSettings</c>) and the dev tools of Development builds (network overlay, delay and loss simulator).
    /// Values are written by DataSeeder (D-010); converted by <c>NetConfigConverter</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NetConfig", menuName = "Arena/Net Config")]
    public sealed class NetConfig : ScriptableObject
    {
        [Tooltip("Content pack ArenaContent.bytes (the same file the server loads).")]
        public TextAsset ContentPack;

        [Tooltip("Server regions in menu order; the first is the default.")]
        public List<NetRegionConfig> Regions = new List<NetRegionConfig>();

        [Header("Prediction clock (Server-Spec §2.3)")]
        [Min(0)] public int TargetInputMarginTicks;
        [Min(0f)] public float MarginGain;
        [Min(0f)] public float MaxTimeDilation;
        [Min(1)] public int HardResyncTicks;
        [Min(0)] public int MarginSettleTicks;
        [Min(1)] public int MaxResimTicks;
        [Min(1)] public int MaxTicksPerFrame;
        [Min(0)] public int FallbackRttMs;

        [Header("Own fighter correction (§2.5)")]
        [Min(0f)] public float CorrectionSmoothingSeconds;
        [Min(0f)] public float SnapDistanceCells;
        [Min(0f)] public float LargeCorrectionCells;

        [Header("Interpolation of others (§2.6)")]
        [Min(0f)] public float InterpDelaySnapshots;
        [Min(0f)] public float InterpJitterFactor;
        [Range(0f, 1f)] public float InterpJitterSmoothing;
        [Min(0f)] public float InterpGain;
        [Min(0)] public int InterpDelayMaxMs;
        [Min(0)] public int MaxExtrapolationMs;

        [Header("Lobby (§6.3)")]
        [Min(1)] public int PingSamples;
        [Min(0)] public int PingIntervalMs;
        [Min(1)] public int PingTimeoutMs;
        [Min(1)] public int PingGoodMs;
        [Min(1)] public int PingFairMs;
        [Min(1)] public int DiscoverRepeats;
        [Min(0)] public int DiscoverIntervalMs;
        [Min(1)] public int DiscoverTimeoutMs;

        [Header("Rejoin (§7)")]
        [Tooltip("A saved rejoin ticket is valid this long (GDD §8.3).")]
        [Min(0f)] public float ReconnectWindowSeconds;

        [Header("Match HUD (S3.11, §12.4)")]
        [Tooltip("Seconds without a packet from the server before the HUD shows 'connection lost'.")]
        [Min(0.1f)] public float ConnectionLostSeconds;

        [Tooltip("The ping badge of the match HUD refreshes this often, seconds.")]
        [Min(0.05f)] public float HudPingRefreshSeconds;

        [Header("Dev tools — Development builds only (§12.3)")]
        [Tooltip("Network overlay in the match HUD: RTT, jitter, loss, input margin, interpolation delay, corrections.")]
        public bool DevOverlay;
        [Min(0.05f)] public float DevOverlayRefreshSeconds;

        [Tooltip("Delay and loss simulator on the client socket.")]
        public bool DevSimEnabled;
        [Tooltip("Added one-way delay, ms (each direction; RTT grows by twice this).")]
        [Min(0)] public int DevSimLatencyMs;
        [Tooltip("Random extra one-way delay 0…this, ms.")]
        [Min(0)] public int DevSimJitterMs;
        [Range(0f, 100f)] public float DevSimLossPercent;
    }
}
