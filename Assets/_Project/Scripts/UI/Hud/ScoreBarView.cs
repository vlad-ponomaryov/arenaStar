using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Gameplay.Settings;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Acorns score bar at the top of the match HUD: acorns of the local team on the left (blue), of the enemies on the
    /// right (red), the time left in the middle, and the hold countdown under it while one runs ("We hold" / "Enemy holds"
    /// + seconds). Texts change only when the value changes; numbers come from <see cref="NumberStringCache"/> (no allocations
    /// per frame). Hidden when the match has no Acorns mode.
    /// </summary>
    public sealed class ScoreBarView
    {
        private readonly GameObject _root;
        private readonly Text _ally;
        private readonly Text _enemy;
        private readonly Text _timer;
        private readonly GameObject _countdownRoot;
        private readonly Text _countdownLabel;
        private readonly Text _countdownNumber;

        private int _shownAlly = -1;
        private int _shownEnemy = -1;
        private int _shownSeconds = -1;
        private int _shownCountdown = -1;
        private int _shownCountdownTeam = AcornsRules.NoTeam;

        public GameObject Root => _root;
        public Text AllyText => _ally;
        public Text EnemyText => _enemy;
        public Text TimerText => _timer;
        public Text CountdownNumber => _countdownNumber;
        public bool CountdownShown => _countdownRoot.activeSelf;

        private ScoreBarView(GameObject root, Text ally, Text enemy, Text timer, GameObject countdownRoot, Text countdownLabel,
            Text countdownNumber)
        {
            _root = root;
            _ally = ally;
            _enemy = enemy;
            _timer = timer;
            _countdownRoot = countdownRoot;
            _countdownLabel = countdownLabel;
            _countdownNumber = countdownNumber;
        }

        public static ScoreBarView Build(Transform canvasRoot, UiStyle style)
        {
            RectTransform root = UiFactory.CreateRect("ScoreBar", canvasRoot);
            root.anchorMin = new Vector2(0.5f, 1f);
            root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -style.ScoreBarTopMargin);
            root.sizeDelta = new Vector2(style.ScoreBoxSize.x * 2f + style.TimerBoxSize.x + style.ScoreBarSpacing * 2f,
                style.ScoreBoxSize.y + style.CountdownSize.y);

            RectTransform row = UiFactory.CreateRow("Row", root, style.ScoreBarSpacing, style.ScoreBoxSize.y);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, style.ScoreBoxSize.y);
            row.anchoredPosition = Vector2.zero;

            Text ally = CreateBox("Ally", row, style, style.ScoreBoxSize, style.AllyColor);
            Text timer = CreateBox("Timer", row, style, style.TimerBoxSize, style.ScorePanelColor);
            Text enemy = CreateBox("Enemy", row, style, style.ScoreBoxSize,
                style.Enemy(PlayerSettingsStore.Shared.Current.ColorblindPalette));

            RectTransform countdown = UiFactory.CreateRow("Countdown", root, style.ScoreBarSpacing, 0f);
            countdown.anchorMin = new Vector2(0f, 0f);
            countdown.anchorMax = new Vector2(1f, 0f);
            countdown.pivot = new Vector2(0.5f, 0f);
            countdown.sizeDelta = new Vector2(0f, style.CountdownSize.y);
            countdown.anchoredPosition = Vector2.zero;
            Text label = UiFactory.CreateLayoutText("Label", countdown, style, style.MediumFontSize, style.CountdownSize, string.Empty);
            Text number = UiFactory.CreateLayoutText("Seconds", countdown, style, style.LargeFontSize,
                new Vector2(style.ScoreBoxSize.x, style.CountdownSize.y), string.Empty);
            countdown.gameObject.SetActive(false);

            return new ScoreBarView(root.gameObject, ally, enemy, timer, countdown.gameObject, label, number);
        }

        /// <summary>Updates the texts from the state (call every frame).</summary>
        public void Apply(MatchDefs defs, MatchState state, int localTeam)
        {
            if (defs == null || state == null) return;
            bool acorns = defs.Mode == ModeKind.Acorns;
            if (_root.activeSelf != acorns) _root.SetActive(acorns);
            if (!acorns) return;

            // Team score from the mode state (S3.5): a hidden carrier is not in an online presentation state.
            ref ModeState mode = ref state.Mode;
            SetNumber(_ally, ref _shownAlly, localTeam == 0 ? mode.Team0Acorns : mode.Team1Acorns);
            SetNumber(_enemy, ref _shownEnemy, localTeam == 0 ? mode.Team1Acorns : mode.Team0Acorns);

            int seconds = CeilSeconds(AcornsRules.TimeLeftTicks(state, defs));
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _timer.text = NumberStringCache.Clock(seconds);
            }

            int team = state.Mode.IsEnded ? AcornsRules.NoTeam : state.Mode.CountdownTeam;
            bool counting = team != AcornsRules.NoTeam;
            if (_countdownRoot.activeSelf != counting) _countdownRoot.SetActive(counting);
            if (!counting)
            {
                _shownCountdownTeam = AcornsRules.NoTeam;
                return;
            }
            if (team != _shownCountdownTeam)
            {
                _shownCountdownTeam = team;
                _countdownLabel.text = Loc.Get(team == localTeam ? LocKeys.HudHoldAlly : LocKeys.HudHoldEnemy);
            }
            SetNumber(_countdownNumber, ref _shownCountdown, CeilSeconds(AcornsRules.CountdownTicksLeft(state)));
        }

        /// <summary>Ticks → whole seconds rounded up (a countdown shows 12 … 1).</summary>
        public static int CeilSeconds(int ticks) => ticks <= 0 ? 0 : (ticks + SimLimits.TickRate - 1) / SimLimits.TickRate;

        private static void SetNumber(Text text, ref int shown, int value)
        {
            if (value == shown) return;
            shown = value;
            text.text = NumberStringCache.Get(value);
        }

        private static Text CreateBox(string name, Transform parent, UiStyle style, Vector2 size, Color color)
        {
            Image box = UiFactory.CreateImage(name, parent, color);
            UiFactory.SetPreferredSize(box.gameObject, size);
            Text text = UiFactory.CreateText("Text", box.transform, style, style.LargeFontSize, TextAnchor.MiddleCenter);
            UiFactory.Stretch(text.rectTransform);
            return text;
        }
    }
}
