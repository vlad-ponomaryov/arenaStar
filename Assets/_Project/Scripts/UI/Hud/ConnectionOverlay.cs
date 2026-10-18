using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Gameplay.Flow;
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
    /// Connection banner of an online match (Server-Spec §7, §12.4; S3.11–S3.12), below the score bar: "connection lost —
    /// reconnecting… N s" when no packet came for <c>NetConfig.ConnectionLostSeconds</c> or the connection is down (N — what is
    /// left of the return window, GDD §8.3, while the client retries by itself); when the attempts gave up — the same text with
    /// a "Back to the match" button (a new window); when the server answered <c>MatchOver</c> — "the match is already over" with
    /// a "Menu" button; otherwise "a bot is playing your fighter" while the snapshots carry the bot flag. Hidden when all is well
    /// or the match is over. Texts change only with the state and the whole seconds (cached number strings) — no allocations
    /// per frame.
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
        private readonly Button _button;
        private readonly Text _buttonLabel;
        private int _shownSeconds = -1;

        private ConnectionOverlay(OnlineMatchDriver driver, float lostSeconds, float windowSeconds, GameObject root, Text message,
            Text seconds, Button button, Text buttonLabel)
        {
            _driver = driver;
            _lostMicros = (long)(lostSeconds * MicrosPerSecond);
            _windowSeconds = windowSeconds;
            _root = root;
            _message = message;
            _seconds = seconds;
            _button = button;
            _buttonLabel = buttonLabel;
            _button.onClick.AddListener(OnButton);
            _button.gameObject.SetActive(false);
        }

        public ConnectionOverlayState State { get; private set; }

        public bool IsShown => _root.activeSelf;

        public Text Message => _message;

        /// <summary>"Back to the match" (gave up) or "Menu" (match over); hidden in the other states.</summary>
        public Button Button => _button;

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
            // The button hangs below the banner (the line is full of text).
            Button button = UiFactory.CreateButton("ConnectionButton", rt, style, style.MenuButtonSize, style.PrimaryButtonColor,
                style.MediumFontSize, string.Empty, null, out Text label);
            RectTransform brt = (RectTransform)button.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -style.MenuItemSpacing);
            panel.gameObject.SetActive(false);
            return new ConnectionOverlay(driver, lostSeconds, windowSeconds, panel.gameObject, message, seconds, button, label);
        }

        public void Tick()
        {
            long now = NetClock.NowMicros();
            ConnectionOverlayState state = ConnectionOverlayState.Ok;
            long silence = 0L;
            ReconnectState reconnect = _driver.ReconnectState;
            if (!_driver.IsEnded)
            {
                silence = _driver.SilenceMicros(now);
                if (reconnect == ReconnectState.MatchOver) state = ConnectionOverlayState.MatchOver;
                else if (reconnect == ReconnectState.GaveUp) state = ConnectionOverlayState.GaveUp;
                else if (!_driver.IsConnected || silence >= _lostMicros) state = ConnectionOverlayState.Lost;
                else if (_driver.IsBotControlled) state = ConnectionOverlayState.BotPlaying;
            }
            if (state != State)
            {
                State = state;
                _root.SetActive(state != ConnectionOverlayState.Ok);
                if (state != ConnectionOverlayState.Ok) _message.text = Loc.Get(MessageKey(state));
                bool button = state == ConnectionOverlayState.GaveUp || state == ConnectionOverlayState.MatchOver;
                _button.gameObject.SetActive(button);
                if (button) _buttonLabel.text = Loc.Get(state == ConnectionOverlayState.GaveUp ? LocKeys.NetRejoin : LocKeys.ResultsMenu);
                _shownSeconds = -1;
                if (state != ConnectionOverlayState.Lost) _seconds.text = string.Empty;
            }
            if (state != ConnectionOverlayState.Lost) return;
            float leftSeconds = reconnect == ReconnectState.Waiting || reconnect == ReconnectState.Connecting
                ? _driver.ReconnectSecondsLeft(now)
                : _windowSeconds - silence / MicrosPerSecond;
            int left = Mathf.CeilToInt(leftSeconds);
            if (left < 0) left = 0;
            if (left == _shownSeconds) return;
            _shownSeconds = left;
            _seconds.text = NumberStringCache.Get(left);
        }

        private static string MessageKey(ConnectionOverlayState state)
        {
            switch (state)
            {
                case ConnectionOverlayState.BotPlaying: return LocKeys.NetBotPlaying;
                case ConnectionOverlayState.GaveUp: return LocKeys.ErrorServerUnreachable;
                case ConnectionOverlayState.MatchOver: return LocKeys.ErrorMatchOver;
                default: return LocKeys.NetReconnecting;
            }
        }

        private void OnButton()
        {
            if (State == ConnectionOverlayState.GaveUp) _driver.RetryRejoin();
            else if (State == ConnectionOverlayState.MatchOver) SceneFlow.ToMenu();
        }
    }
}
