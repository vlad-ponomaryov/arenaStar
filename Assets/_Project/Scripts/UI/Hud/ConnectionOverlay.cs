using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Net.Transport;
using Arena.Online;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Connection banner of an online match (Server-Spec §7, §12.4; S3.11), below the score bar: "connection lost — reconnecting…
    /// N s" when no packet came for <c>NetConfig.ConnectionLostSeconds</c> or the connection is down (N — what is left of the
    /// reconnect window, GDD §8.3; the reconnect itself comes with S3.12), otherwise "a bot is playing your fighter" while the
    /// snapshots carry the bot flag. Hidden when all is well or the match is over. Texts change only with the state and the
    /// whole seconds (cached number strings) — no allocations per frame.
    /// </summary>
    public sealed class ConnectionOverlay
    {
        private const float MicrosPerSecond = 1000000f;

        private readonly OnlineMatchDriver _driver;
        private readonly long _lostMicros;
        private readonly float _windowSeconds;
        private readonly GameObject _root;
        private readonly Text _message;
        private readonly Text _seconds;
        private int _shownSeconds = -1;

        private ConnectionOverlay(OnlineMatchDriver driver, float lostSeconds, float windowSeconds, GameObject root, Text message,
            Text seconds)
        {
            _driver = driver;
            _lostMicros = (long)(lostSeconds * MicrosPerSecond);
            _windowSeconds = windowSeconds;
            _root = root;
            _message = message;
            _seconds = seconds;
        }

        public ConnectionOverlayState State { get; private set; }

        public bool IsShown => _root.activeSelf;

        public Text Message => _message;

        public static ConnectionOverlay Build(Transform canvas, UiStyle style, OnlineMatchDriver driver, float lostSeconds,
            float windowSeconds)
        {
            Image panel = UiFactory.CreateImage("ConnectionOverlay", canvas, style.OverlayPanelColor);
            RectTransform rt = panel.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = style.OverlayBoxSize;
            rt.anchoredPosition = new Vector2(0f, -style.OverlayTopMargin);
            RectTransform row = UiFactory.CreateRow("Row", rt, style.MenuItemSpacing, 0f);
            UiFactory.Stretch(row);
            var lineSize = new Vector2(style.OverlayBoxSize.x - style.ScoreBoxSize.x, style.OverlayBoxSize.y);
            Text message = UiFactory.CreateLayoutText("Message", row, style, style.MediumFontSize, lineSize, string.Empty);
            Text seconds = UiFactory.CreateLayoutText("Seconds", row, style, style.MediumFontSize,
                new Vector2(style.ScoreBoxSize.x * 0.5f, style.OverlayBoxSize.y), string.Empty);
            panel.gameObject.SetActive(false);
            return new ConnectionOverlay(driver, lostSeconds, windowSeconds, panel.gameObject, message, seconds);
        }

        public void Tick()
        {
            long now = NetClock.NowMicros();
            ConnectionOverlayState state = ConnectionOverlayState.Ok;
            long silence = 0L;
            if (!_driver.IsEnded)
            {
                silence = _driver.SilenceMicros(now);
                if (!_driver.IsConnected || silence >= _lostMicros) state = ConnectionOverlayState.Lost;
                else if (_driver.IsBotControlled) state = ConnectionOverlayState.BotPlaying;
            }
            if (state != State)
            {
                State = state;
                _root.SetActive(state != ConnectionOverlayState.Ok);
                if (state != ConnectionOverlayState.Ok)
                    _message.text = Loc.Get(state == ConnectionOverlayState.Lost ? LocKeys.NetReconnecting : LocKeys.NetBotPlaying);
                _shownSeconds = -1;
                if (state != ConnectionOverlayState.Lost) _seconds.text = string.Empty;
            }
            if (state != ConnectionOverlayState.Lost) return;
            int left = Mathf.CeilToInt(_windowSeconds - silence / MicrosPerSecond);
            if (left < 0) left = 0;
            if (left == _shownSeconds) return;
            _shownSeconds = left;
            _seconds.text = NumberStringCache.Get(left);
        }
    }
}
