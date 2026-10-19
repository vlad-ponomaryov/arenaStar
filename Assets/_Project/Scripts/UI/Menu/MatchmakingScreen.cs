using System;
using Arena.Core.Modes;
using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Net.Client;
using Arena.Online;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Menu
{
    /// <summary>
    /// Matchmaking screen over the menu (Server-Spec §6.2, §12.4; S3.11), shown while the <see cref="OnlineSession"/> locates the
    /// server, connects or waits in the queue: the mode, "Connecting…" / "Searching for players…", the waiting clock (the
    /// server's <c>QueueStatus</c> once a second, the local clock before it), "Found N of M" and "Cancel". The backdrop blocks
    /// the menu. Texts change only when the values do (the menu is not a hot path; "Found N of M" is formatted on a change).
    /// </summary>
    public sealed class MatchmakingScreen
    {
        /// <summary>Above the menu canvas, below the settings panel.</summary>
        public const int SortingOrder = 5;

        private readonly Canvas _canvas;
        private readonly Text _mode;
        private readonly Text _status;
        private readonly Text _clock;
        private readonly Text _found;
        private ModeKind _shownMode = (ModeKind)byte.MaxValue;
        private string _shownStatusKey;
        private int _shownSeconds = -1;
        private int _shownFound = -1;
        private int _shownNeeded = -1;

        private MatchmakingScreen(Canvas canvas, Text mode, Text status, Text clock, Text found, Button cancel)
        {
            _canvas = canvas;
            _mode = mode;
            _status = status;
            _clock = clock;
            _found = found;
            CancelButton = cancel;
        }

        public bool IsShown => _canvas.gameObject.activeSelf;
        public Canvas Canvas => _canvas;
        public Button CancelButton { get; }
        public Text StatusText => _status;
        public Text ClockText => _clock;
        public Text FoundText => _found;

        public static MatchmakingScreen Build(Transform parent, UiStyle s, Action onCancel)
        {
            Canvas canvas = UiFactory.CreateCanvas("MatchmakingCanvas", parent, s, SortingOrder);
            // A.6: the menu backdrop under a cream panel: plank with the mode, status, big clock, "Found N of M", "Cancel".
            UiFactory.CreateBackdrop(canvas.transform, s, s.MatchmakingBackdropColor, true);
            RectTransform outer = UiFactory.CreateColumn("Outer", canvas.transform, 0f);
            UiFactory.Stretch(outer);
            RectTransform column = UiFactory.CreatePanel("Column", outer, s, s.MenuRowSpacing * 0.5f, true);
            var line = new Vector2(s.PlayButtonSize.x * 2f, s.PlayButtonSize.y * 0.6f);
            Text mode = UiFactory.CreatePlankTitle("Mode", column, s, line.x, s.TitleFontSize, string.Empty);
            Text status = UiFactory.CreatePanelText("Status", column, s, s.LargeFontSize, line, string.Empty);
            Text clock = UiFactory.CreatePanelText("Clock", column, s, s.TitleFontSize, line, string.Empty);
            Text found = UiFactory.CreatePanelText("Found", column, s, s.MediumFontSize, line, string.Empty);
            Button cancel = UiFactory.CreateButton("CancelButton", column, s, s.PlayButtonSize, s.SecondaryButtonColor, s.LargeFontSize,
                Loc.Get(LocKeys.QueueCancel), () => onCancel?.Invoke(), out _);
            canvas.gameObject.SetActive(false);
            return new MatchmakingScreen(canvas, mode, status, clock, found, cancel);
        }

        /// <summary>Shows or hides the screen by the session phase and refreshes its texts (call every frame).</summary>
        public void Tick(OnlineSession online)
        {
            bool busy = online != null && online.IsBusy;
            if (IsShown != busy)
            {
                _canvas.gameObject.SetActive(busy);
                _shownSeconds = -1;
                _shownFound = -1;
                _shownNeeded = -1;
            }
            if (!busy) return;

            ModeKind mode = online.SearchMode;
            if (mode != _shownMode)
            {
                _shownMode = mode;
                _mode.text = Loc.Get(mode == ModeKind.LastBeast ? LocKeys.ModeLastBeast : LocKeys.ModeAcorns);
            }
            bool searching = online.Phase == OnlinePhase.Searching || online.Phase == OnlinePhase.Starting;
            string statusKey = searching ? LocKeys.QueueSearching : LocKeys.NetConnecting;
            if (!ReferenceEquals(statusKey, _shownStatusKey))
            {
                _shownStatusKey = statusKey;
                _status.text = Loc.Get(statusKey);
            }

            LobbyClient lobby = online.Lobby;
            bool hasStatus = searching && lobby != null && lobby.HasStatus;
            int seconds = hasStatus ? lobby.Status.WaitSeconds : online.SearchSeconds;
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _clock.text = NumberStringCache.Clock(seconds);
            }
            int foundCount = hasStatus ? lobby.Status.Found : 0;
            int needed = hasStatus ? lobby.Status.Needed : 0;
            if (foundCount == _shownFound && needed == _shownNeeded) return;
            _shownFound = foundCount;
            _shownNeeded = needed;
            _found.text = needed > 0 ? string.Format(Loc.Get(LocKeys.QueueFound), NumberStringCache.Get(foundCount),
                NumberStringCache.Get(needed)) : string.Empty;
        }
    }
}
