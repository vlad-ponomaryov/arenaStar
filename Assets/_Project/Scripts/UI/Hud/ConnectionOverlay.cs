using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Gameplay.Flow;
using Arena.Net.Client;
using Arena.Net.Transport;
using Arena.Online;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Connection banner of an online match (Server-Spec §7, §12.4; S3.11–S3.12), below the score bar: "connection lost —
    /// reconnecting… N s" when no packet came for <c>NetConfig.ConnectionLostSeconds</c> or the connection is down (N — what is
    /// left of the return window, GDD §8.3, while the client retries by itself); when the attempts gave up, or the connection is
    /// down with nothing retrying (the server closed it) — "server unreachable" with a "Back to the match" button (a new window;
    /// only with a rejoin ticket); when the server answered <c>MatchOver</c> — "the match is already over". With every banner
    /// except "a bot is playing your fighter" a "Menu" button (and Escape / Android Back) leaves the match at once:
    /// <see cref="SceneFlow.ToMenu"/> → <c>OnlineSession</c> sends <c>Leave</c>, closes the connection and forgets the ticket
    /// (F1, D-116). Hidden when all is well or the match is over. Texts change only with the state and the whole seconds
    /// (cached number strings) — no allocations per frame.
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
        private readonly Button _menuButton;
        private readonly float _buttonOffset;
        private int _shownSeconds = -1;
        private bool _rejoinShown;
        private bool _leaving;

        private ConnectionOverlay(OnlineMatchDriver driver, float lostSeconds, float windowSeconds, GameObject root, Text message,
            Text seconds, Button button, Text buttonLabel, Button menuButton, float buttonOffset)
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
            _menuButton = menuButton;
            _buttonOffset = buttonOffset;
            _menuButton.onClick.AddListener(OnMenu);
            _menuButton.gameObject.SetActive(false);
        }

        public ConnectionOverlayState State { get; private set; }

        public bool IsShown => _root.activeSelf;

        public Text Message => _message;

        /// <summary>"Back to the match" (gave up, with a rejoin ticket); hidden in the other states.</summary>
        public Button Button => _button;

        /// <summary>"Menu": shown with every banner except <see cref="ConnectionOverlayState.BotPlaying"/>.</summary>
        public Button MenuButton => _menuButton;

        public static ConnectionOverlay Build(Transform canvas, UiStyle style, OnlineMatchDriver driver, float lostSeconds,
            float windowSeconds)
        {
            // A.6: cream panel with a wooden frame and dark text (a plain dark box without the skin sprites).
            bool skinned = style.PanelSprite != null;
            Image panel = UiFactory.CreateSliced("ConnectionOverlay", canvas, style.PanelSprite,
                skinned ? Color.white : style.OverlayPanelColor, style.SliceScale);
            RectTransform rt = panel.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = style.OverlayBoxSize;
            rt.anchoredPosition = new Vector2(0f, -style.OverlayTopMargin);
            RectTransform row = UiFactory.CreateRow("Row", rt, style.MenuItemSpacing, 0f);
            UiFactory.Stretch(row);
            var lineSize = new Vector2(style.OverlayBoxSize.x - style.ScoreBoxSize.x, style.OverlayBoxSize.y);
            Text message = UiFactory.CreateLayoutText("Message", row, style, style.MediumFontSize, lineSize, string.Empty);
            Text seconds = UiFactory.CreateLayoutText("Seconds", row, style, style.LargeFontSize,
                new Vector2(style.ScoreBoxSize.x * 0.5f, style.OverlayBoxSize.y), string.Empty);
            if (skinned)
            {
                UiFactory.MakeDark(message, style);
                UiFactory.MakeDark(seconds, style);
            }
            // The buttons hang below the banner (the line is full of text): side by side, or one centred.
            Button button = UiFactory.CreateButton("ConnectionButton", rt, style, style.MenuButtonSize, style.PrimaryButtonColor,
                style.MediumFontSize, string.Empty, null, out Text label);
            PlaceBelow((RectTransform)button.transform, style.MenuItemSpacing);
            Button menu = UiFactory.CreateButton("ConnectionMenuButton", rt, style, style.MenuButtonSize, style.ButtonColor,
                style.MediumFontSize, Loc.Get(LocKeys.ResultsMenu), null, out _);
            PlaceBelow((RectTransform)menu.transform, style.MenuItemSpacing);
            float offset = (style.MenuButtonSize.x + style.MenuItemSpacing) * 0.5f;
            panel.gameObject.SetActive(false);
            return new ConnectionOverlay(driver, lostSeconds, windowSeconds, panel.gameObject, message, seconds, button, label, menu,
                offset);
        }

        /// <summary>
        /// The banner state (pure, for tests): the match over — nothing; <c>MatchOver</c> / <c>GaveUp</c> as the client says; the
        /// connection down with no automatic attempt running (<see cref="ReconnectState.None"/>: closed by the server, no ticket) —
        /// <see cref="ConnectionOverlayState.GaveUp"/> (a countdown would only run to 0); down or silent while retrying — lost.
        /// </summary>
        public static ConnectionOverlayState Classify(bool matchEnded, ReconnectState reconnect, bool connected, long silenceMicros,
            long lostMicros, bool botControlled)
        {
            if (matchEnded) return ConnectionOverlayState.Ok;
            if (reconnect == ReconnectState.MatchOver) return ConnectionOverlayState.MatchOver;
            if (reconnect == ReconnectState.GaveUp) return ConnectionOverlayState.GaveUp;
            if (!connected && reconnect == ReconnectState.None) return ConnectionOverlayState.GaveUp;
            if (!connected || silenceMicros >= lostMicros) return ConnectionOverlayState.Lost;
            return botControlled ? ConnectionOverlayState.BotPlaying : ConnectionOverlayState.Ok;
        }

        /// <summary>The "Menu" button goes with a banner state (everything but the bot notice).</summary>
        public static bool ShowsMenu(ConnectionOverlayState state) =>
            state == ConnectionOverlayState.Lost || state == ConnectionOverlayState.GaveUp || state == ConnectionOverlayState.MatchOver;

        public void Tick()
        {
            long now = NetClock.NowMicros();
            bool ended = _driver.IsEnded;
            long silence = ended ? 0L : _driver.SilenceMicros(now);
            ReconnectState reconnect = _driver.ReconnectState;
            ConnectionOverlayState state = Classify(ended, reconnect, _driver.IsConnected, silence, _lostMicros, _driver.IsBotControlled);
            bool rejoin = state == ConnectionOverlayState.GaveUp && _driver.CanRetryRejoin;
            if (state != State || rejoin != _rejoinShown)
            {
                State = state;
                _rejoinShown = rejoin;
                _root.SetActive(state != ConnectionOverlayState.Ok);
                if (state != ConnectionOverlayState.Ok) _message.text = Loc.Get(MessageKey(state));
                _menuButton.gameObject.SetActive(ShowsMenu(state));
                _button.gameObject.SetActive(rejoin);
                if (rejoin) _buttonLabel.text = Loc.Get(LocKeys.NetRejoin);
                SetX((RectTransform)_button.transform, rejoin ? -_buttonOffset : 0f);
                SetX((RectTransform)_menuButton.transform, rejoin ? _buttonOffset : 0f);
                _shownSeconds = -1;
                if (state != ConnectionOverlayState.Lost) _seconds.text = string.Empty;
            }
            // Escape / Android Back (the Input System reports Back as Escape) while the "Menu" button is up.
            if (_menuButton.gameObject.activeSelf)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) OnMenu();
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

        private static void PlaceBelow(RectTransform rt, float spacing)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -spacing);
        }

        private static void SetX(RectTransform rt, float x) => rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);

        private void OnButton()
        {
            if (State == ConnectionOverlayState.GaveUp) _driver.RetryRejoin();
        }

        /// <summary>Leaves at once in any state: the scene change makes <c>OnlineSession</c> send Leave and close the session.</summary>
        private void OnMenu()
        {
            if (_leaving) return;
            _leaving = true;
            SceneFlow.ToMenu();
        }
    }
}
