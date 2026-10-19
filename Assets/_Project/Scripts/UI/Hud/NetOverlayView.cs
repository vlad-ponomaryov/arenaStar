using System.Text;
using Arena.Core.Sim;
using Arena.Data.Localization;
using Arena.Net.Client;
using Arena.Net.Transport;
using Arena.Online;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Dev network overlay of an online match (Server-Spec §12.3; Development builds only, <c>NetConfig.DevOverlay</c>): RTT and
    /// its variation, packets not acknowledged (≈ loss), the server's input margin, the interpolation delay and the own
    /// corrections (non-zero / large), refreshed a few times a second. Builds its text with a reused <see cref="StringBuilder"/>;
    /// the one string per refresh is a dev-build-only allocation (D-101).
    /// </summary>
    public sealed class NetOverlayView
    {
        private const float MicrosPerMs = 1000f;
        private const float PercentScale = 100f;
        private const float MsPerTick = 1000f / SimLimits.TickRate;

        private readonly Text _text;
        private readonly OnlineMatchDriver _driver;
        private readonly float _refreshSeconds;
        private readonly StringBuilder _builder = new StringBuilder();
        private float _untilRefresh;

        private NetOverlayView(Text text, OnlineMatchDriver driver, float refreshSeconds)
        {
            _text = text;
            _driver = driver;
            _refreshSeconds = refreshSeconds;
        }

        public Text Text => _text;

        /// <summary>Refreshes done (tests).</summary>
        public int Refreshes { get; private set; }

        public static NetOverlayView Build(Transform canvas, UiStyle style, OnlineMatchDriver driver, float refreshSeconds)
        {
            Text text = UiFactory.CreateText("NetOverlay", canvas, style, style.SmallFontSize, TextAnchor.UpperRight);
            RectTransform rt = text.rectTransform;
            rt.anchorMin = Vector2.one;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.one;
            // Below the pause button of the top-right corner (D-129).
            rt.anchoredPosition = new Vector2(-style.VersionMargin.x,
                -(style.PauseButtonMargin.y + style.PauseButtonSize.y + style.VersionMargin.y));
            rt.sizeDelta = new Vector2(style.PlayButtonSize.x * 2f, style.PlayButtonSize.y);
            text.raycastTarget = false;
            return new NetOverlayView(text, driver, refreshSeconds);
        }

        public void Tick(float deltaTime)
        {
            _untilRefresh -= deltaTime;
            if (_untilRefresh > 0f) return;
            _untilRefresh = _refreshSeconds;
            Refreshes++;

            MatchClientSession session = _driver.Session;
            NetConnection connection = session.Client.Transport.Connection;
            RttEstimator rtt = connection.Rtt;
            long sent = connection.PacketsSent;
            float lost = sent > 0 ? (sent - connection.PacketsAcked) * PercentScale / sent : 0f;
            int margin = session.Latest != null ? session.Latest.Header.InputMarginMin : 0;
            float interpMs = session.Interpolation.DelayTicks * MsPerTick;
            PredictionErrorStats errors = session.Predictor != null ? session.Predictor.Errors : null;
            string ms = Loc.Get(LocKeys.DevNetMs);

            StringBuilder b = _builder;
            b.Length = 0;
            b.Append(Loc.Get(LocKeys.DevNetRtt)).Append(' ').Append(Mathf.RoundToInt(rtt.SmoothedMicros / MicrosPerMs)).Append(' ').Append(ms)
                .Append("  ").Append(Loc.Get(LocKeys.DevNetJitter)).Append(' ').Append(Mathf.RoundToInt(rtt.VarianceMicros / MicrosPerMs))
                .Append(' ').Append(ms).Append("  ").Append(Loc.Get(LocKeys.DevNetLoss)).Append(' ').Append(Mathf.RoundToInt(lost))
                .Append('\n').Append(Loc.Get(LocKeys.DevNetMargin)).Append(' ').Append(margin)
                .Append("  ").Append(Loc.Get(LocKeys.DevNetInterp)).Append(' ').Append(Mathf.RoundToInt(interpMs)).Append(' ').Append(ms)
                .Append("  ").Append(Loc.Get(LocKeys.DevNetCorrections)).Append(' ')
                .Append(errors != null ? errors.NonZeroSamples : 0L).Append('/').Append(errors != null ? errors.LargeCorrections : 0L);
            if (OnlineSession.Instance != null && OnlineSession.Instance.Simulating)
                b.Append("  ").Append(Loc.Get(LocKeys.DevNetSimulator));
            _text.text = b.ToString();
        }
    }
}
