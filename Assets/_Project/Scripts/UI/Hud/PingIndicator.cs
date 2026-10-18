using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Net.Client;
using Arena.Online;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Ping badge of an online match (Server-Spec §6.3, §12.4; GDD §8.2), under the FPS counter: the smoothed round trip of the
    /// connection in ms, green below <c>PingGoodMs</c>, yellow below <c>PingFairMs</c>, red from it (grey before the first
    /// sample). Refreshed every <c>NetConfig.HudPingRefreshSeconds</c>; the number is a cached string — no allocations per frame.
    /// </summary>
    public sealed class PingIndicator
    {
        private readonly OnlineMatchDriver _driver;
        private readonly LobbyClientSettings _thresholds;
        private readonly UiStyle _style;
        private readonly float _refreshSeconds;
        private readonly Text _value;
        private readonly Text _unit;
        private float _untilRefresh;

        private PingIndicator(OnlineMatchDriver driver, LobbyClientSettings thresholds, UiStyle style, float refreshSeconds, Text value,
            Text unit)
        {
            _driver = driver;
            _thresholds = thresholds;
            _style = style;
            _refreshSeconds = refreshSeconds;
            _value = value;
            _unit = unit;
        }

        /// <summary>Last shown ping, ms (−1 — none yet; tests).</summary>
        public int ShownMs { get; private set; } = -1;

        public PingQuality ShownQuality { get; private set; }

        public Text Value => _value;

        public static PingIndicator Build(Transform canvas, UiStyle style, OnlineMatchDriver driver, LobbyClientSettings thresholds,
            float refreshSeconds)
        {
            RectTransform box = UiFactory.CreateRect("PingIndicator", canvas);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
            box.sizeDelta = style.PingBoxSize;
            box.anchoredPosition = new Vector2(style.PingMargin.x, -(style.FpsMargin.y + style.FpsBoxSize.y + style.PingMargin.y));
            Text value = UiFactory.CreateText("Value", box, style, style.SmallFontSize, TextAnchor.MiddleLeft, string.Empty);
            UiFactory.Stretch(value.rectTransform);
            Text unit = UiFactory.CreateText("Unit", box, style, style.SmallFontSize, TextAnchor.MiddleRight, Loc.Get(LocKeys.HudPingMs));
            UiFactory.Stretch(unit.rectTransform);
            value.color = unit.color = style.PingUnknownColor;
            return new PingIndicator(driver, thresholds, style, refreshSeconds, value, unit);
        }

        public void Tick(float unscaledDeltaTime)
        {
            _untilRefresh -= unscaledDeltaTime;
            if (_untilRefresh > 0f) return;
            _untilRefresh = _refreshSeconds;
            int ms = _driver.RttMs;
            if (ms <= 0) ms = -1; // no sample yet
            if (ms == ShownMs) return;
            ShownMs = ms;
            _value.text = ms < 0 ? string.Empty : NumberStringCache.Get(ms > NumberStringCache.MaxCachedNumber ? NumberStringCache.MaxCachedNumber : ms);
            PingQuality q = RegionPinger.Quality(ms, _thresholds);
            if (q == ShownQuality) return;
            ShownQuality = q;
            _value.color = _unit.color = _style.Ping(q);
        }
    }
}
