using System;
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

namespace Arena.UI.Results
{
    /// <summary>
    /// End-of-match screen (GDD §5.3): victory / defeat / draw for the local team, the MVP (fighter name and a jumping
    /// capsule in the team colour), statistics of every fighter (takedowns, knockouts, damage, acorns) and the buttons
    /// "Play again" (same launch parameters) and "Menu", both visible at once. Built hidden at match start on its own
    /// canvas above the HUD; <see cref="Show"/> fills it once when the match ends.
    /// </summary>
    public sealed class ResultsScreen
    {
        /// <summary>Above the match HUD canvas.</summary>
        public const int SortingOrder = 10;

        private const float FullTurn = 2f * Mathf.PI;

        private readonly UiStyle _style;
        private readonly Canvas _canvas;
        private readonly RectTransform _content;
        private readonly Action _onAgain;
        private readonly Action _onMenu;

        private RectTransform _capsule;
        private float _shownAt;

        public bool IsShown => _canvas.gameObject.activeSelf;
        public Canvas Canvas => _canvas;
        public Button AgainButton { get; private set; }
        public Button MenuButton { get; private set; }
        public Text TitleText { get; private set; }
        public Text MvpNameText { get; private set; }

        /// <summary>Slot of the MVP shown (−1 if none).</summary>
        public int MvpSlot { get; private set; } = -1;

        private ResultsScreen(UiStyle style, Canvas canvas, RectTransform content, Action onAgain, Action onMenu)
        {
            _style = style;
            _canvas = canvas;
            _content = content;
            _onAgain = onAgain;
            _onMenu = onMenu;
        }

        public static ResultsScreen Build(Transform parent, UiStyle style, Action onAgain, Action onMenu)
        {
            Canvas canvas = UiFactory.CreateCanvas("ResultsCanvas", parent, style, SortingOrder);
            Image backdrop = UiFactory.CreateImage("Backdrop", canvas.transform, style.ResultsBackdropColor, null, true);
            UiFactory.Stretch(backdrop.rectTransform);
            RectTransform content = UiFactory.CreateColumn("Content", canvas.transform, style.ResultsSpacing);
            UiFactory.Stretch(content);
            canvas.gameObject.SetActive(false);
            return new ResultsScreen(style, canvas, content, onAgain, onMenu);
        }

        public void Show(MatchDefs defs, MatchState state, int localTeam)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (state == null) throw new ArgumentNullException(nameof(state));
            UiFactory.DestroyChildren(_content);
            UiStyle s = _style;

            int winner = state.Mode.WinnerTeam;
            string titleKey = winner == AcornsRules.NoTeam ? LocKeys.ResultsDraw
                : winner == localTeam ? LocKeys.ResultsVictory : LocKeys.ResultsDefeat;
            float wide = s.ResultsNameColumnWidth + s.ResultsStatColumnWidth * 4f;
            TitleText = UiFactory.CreateLayoutText("Title", _content, s, s.TitleFontSize, new Vector2(wide, s.TitleFontSize * 1.5f),
                Loc.Get(titleKey));

            MvpSlot = defs.Acorns != null ? MvpCalculator.Compute(state, defs.Acorns) : -1;
            BuildMvp(defs, state, localTeam);
            BuildTable(defs, state, localTeam);

            RectTransform buttons = UiFactory.CreateRow("Buttons", _content, s.ResultsSpacing, s.ResultsButtonSize.y);
            AgainButton = UiFactory.CreateButton("AgainButton", buttons, s, s.ResultsButtonSize, s.PrimaryButtonColor, s.LargeFontSize,
                Loc.Get(LocKeys.ResultsAgain), () => _onAgain?.Invoke(), out _);
            MenuButton = UiFactory.CreateButton("MenuButton", buttons, s, s.ResultsButtonSize, s.ButtonColor, s.LargeFontSize,
                Loc.Get(LocKeys.ResultsMenu), () => _onMenu?.Invoke(), out _);

            _shownAt = Time.unscaledTime;
            _canvas.gameObject.SetActive(true);
        }

        /// <summary>Animates the MVP capsule (call every frame while shown).</summary>
        public void Tick()
        {
            if (!IsShown || _capsule == null) return;
            float t = (Time.unscaledTime - _shownAt) * _style.MvpBounceFrequency;
            float jump = Mathf.Abs(Mathf.Sin(t * FullTurn * 0.5f)) * _style.MvpBounceHeight;
            _capsule.anchoredPosition = new Vector2(0f, jump);
        }

        private void BuildMvp(MatchDefs defs, MatchState state, int localTeam)
        {
            UiStyle s = _style;
            float height = s.MvpCapsuleSize.y + s.MvpBounceHeight;
            RectTransform row = UiFactory.CreateRow("Mvp", _content, s.ResultsSpacing, height);
            if (MvpSlot < 0) return;

            ref FighterState f = ref state.Fighters[MvpSlot];
            RectTransform holder = UiFactory.CreateRect("CapsuleHolder", row);
            UiFactory.SetPreferredSize(holder.gameObject, new Vector2(s.MvpCapsuleSize.x, height));
            Image capsule = UiFactory.CreateImage("Capsule", holder, f.Team == localTeam ? s.AllyColor : EnemyColor(s), s.CircleSprite);
            _capsule = capsule.rectTransform;
            _capsule.anchorMin = new Vector2(0.5f, 0f);
            _capsule.anchorMax = new Vector2(0.5f, 0f);
            _capsule.pivot = new Vector2(0.5f, 0f);
            _capsule.sizeDelta = s.MvpCapsuleSize;
            _capsule.anchoredPosition = Vector2.zero;

            RectTransform names = UiFactory.CreateColumn("MvpText", row, 0f);
            Vector2 lineSize = new Vector2(s.ResultsNameColumnWidth, s.MvpCapsuleSize.y * 0.5f);
            UiFactory.CreateLayoutText("MvpLabel", names, s, s.MediumFontSize, lineSize, Loc.Get(LocKeys.ResultsMvp), TextAnchor.LowerLeft);
            FighterDef def = defs.Fighters[f.DefIndex];
            MvpNameText = UiFactory.CreateLayoutText("MvpName", names, s, s.TitleFontSize, lineSize, Loc.Get(def.NameKey), TextAnchor.UpperLeft);
        }

        private void BuildTable(MatchDefs defs, MatchState state, int localTeam)
        {
            UiStyle s = _style;
            RectTransform table = UiFactory.CreateColumn("Stats", _content, 0f);
            AddRow(table, "Header", Loc.Get(LocKeys.MenuFighter), Loc.Get(LocKeys.ResultsKills), Loc.Get(LocKeys.ResultsDeaths),
                Loc.Get(LocKeys.ResultsDamage), Loc.Get(LocKeys.ResultsAcorns), s.TextColor, s.SmallFontSize);

            int count = state.Globals.FighterCount;
            // Own team first, then the enemies.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    ref FighterState f = ref state.Fighters[i];
                    if (!f.Active || (f.Team == localTeam) != (pass == 0)) continue;
                    ref FighterStats st = ref state.Stats[i];
                    Color color = f.Team == localTeam ? s.AllyColor : EnemyColor(s);
                    AddRow(table, "Row" + i, Loc.Get(defs.Fighters[f.DefIndex].NameKey), NumberStringCache.Get(st.Kills),
                        NumberStringCache.Get(st.Deaths), st.DamageDealt.ToString(), NumberStringCache.Get(st.AcornsCollected), color,
                        s.MediumFontSize);
                }
            }
        }

        private void AddRow(Transform table, string name, string fighter, string kills, string deaths, string damage, string acorns,
            Color color, int fontSize)
        {
            UiStyle s = _style;
            RectTransform row = UiFactory.CreateRow(name, table, 0f, s.ResultsRowHeight);
            var nameSize = new Vector2(s.ResultsNameColumnWidth, s.ResultsRowHeight);
            var statSize = new Vector2(s.ResultsStatColumnWidth, s.ResultsRowHeight);
            UiFactory.CreateLayoutText("Fighter", row, s, fontSize, nameSize, fighter, TextAnchor.MiddleLeft).color = color;
            UiFactory.CreateLayoutText("Kills", row, s, fontSize, statSize, kills);
            UiFactory.CreateLayoutText("Deaths", row, s, fontSize, statSize, deaths);
            UiFactory.CreateLayoutText("Damage", row, s, fontSize, statSize, damage);
            UiFactory.CreateLayoutText("Acorns", row, s, fontSize, statSize, acorns);
        }

        private static Color EnemyColor(UiStyle s) => s.Enemy(PlayerSettingsStore.Shared.Current.ColorblindPalette);
    }
}
