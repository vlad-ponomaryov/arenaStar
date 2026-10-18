using System;
using Arena.Bots;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Gameplay.Flow;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Menu
{
    /// <summary>
    /// Main menu built at runtime from <see cref="UiStyle"/> (D-018): mode name, fighter choice (all fighters of the
    /// database, names by localization key), bot difficulty (Novice / Easy, GDD §7), language EN / RU (saved locally,
    /// <see cref="Loc"/>), "Play" → <see cref="SceneFlow.PlayMatch"/>. The choice survives the trip to the match and back
    /// (taken from <see cref="SceneFlow.Current"/>). Switching the language rebuilds the screen. All texts go through keys.
    /// </summary>
    public sealed class MenuScreen : MonoBehaviour
    {
        private static readonly BotDifficulty[] BotChoices = { BotDifficulty.Novice, BotDifficulty.Easy };

        [SerializeField] private GameDatabase _database;
        [SerializeField] private UiStyle _style;

        private Canvas _canvas;
        private string _fighterId;
        private BotDifficulty _bots;
        private Button[] _fighterButtons = new Button[0];
        private Button[] _botButtons = new Button[0];
        private Button[] _languageButtons = new Button[0];

        public GameDatabase Database => _database;
        public UiStyle Style => _style;
        public string SelectedFighterId => _fighterId;
        public BotDifficulty SelectedBots => _bots;
        public Button PlayButton { get; private set; }

        private void Start()
        {
            if (_database == null || _style == null)
            {
                Debug.LogError("MenuScreen: database or style is not set", this);
                enabled = false;
                return;
            }
            if (_database.Strings != null) Loc.Init(_database.Strings);
            MatchLaunchParams last = SceneFlow.Current;
            _fighterId = last != null ? last.FighterId : DefaultFighterId();
            _bots = last != null ? last.Bots : BotChoices[0];
            Loc.LanguageChanged += Rebuild;
            Rebuild();
        }

        private void OnDestroy()
        {
            Loc.LanguageChanged -= Rebuild;
        }

        public void SelectFighter(string id)
        {
            _fighterId = id;
            RefreshSelection();
        }

        public void SelectBots(BotDifficulty bots)
        {
            _bots = bots;
            RefreshSelection();
        }

        /// <summary>Starts a match with the current choice and a new seed.</summary>
        public void Play()
        {
            SceneFlow.PlayMatch(new MatchLaunchParams(_fighterId, _bots, Environment.TickCount));
        }

        private string DefaultFighterId()
        {
            MatchRunnerConfig runner = _database.MatchRunner;
            if (runner != null && runner.LocalSlot >= 0 && runner.LocalSlot < runner.SlotFighterIds.Count)
                return runner.SlotFighterIds[runner.LocalSlot];
            return _database.Fighters.Count > 0 ? _database.Fighters[0].Id : null;
        }

        private void Rebuild()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            UiStyle s = _style;
            _canvas = UiFactory.CreateCanvas("MenuCanvas", transform, s, 0);
            RectTransform column = UiFactory.CreateColumn("Column", _canvas.transform, s.MenuRowSpacing);
            UiFactory.Stretch(column);

            UiFactory.CreateLayoutText("Mode", column, s, s.TitleFontSize, new Vector2(s.MenuButtonSize.x * 4f, s.MenuButtonSize.y),
                Loc.Get(LocKeys.ModeAcorns));

            int fighters = _database.Fighters.Count;
            _fighterButtons = new Button[fighters];
            RectTransform fighterRow = CreateLabeledRow(column, LocKeys.MenuFighter);
            for (int i = 0; i < fighters; i++)
            {
                FighterConfig f = _database.Fighters[i];
                string id = f.Id;
                _fighterButtons[i] = UiFactory.CreateButton("Fighter_" + id, fighterRow, s, s.MenuButtonSize, s.ButtonColor,
                    s.MediumFontSize, Loc.Get(f.NameKey), () => SelectFighter(id), out _);
            }

            _botButtons = new Button[BotChoices.Length];
            RectTransform botRow = CreateLabeledRow(column, LocKeys.MenuBots);
            for (int i = 0; i < BotChoices.Length; i++)
            {
                BotDifficulty bots = BotChoices[i];
                _botButtons[i] = UiFactory.CreateButton("Bots_" + bots, botRow, s, s.MenuButtonSize, s.ButtonColor, s.MediumFontSize,
                    Loc.Get(bots == BotDifficulty.Novice ? LocKeys.BotsNovice : LocKeys.BotsEasy), () => SelectBots(bots), out _);
            }

            RectTransform languageRow = CreateLabeledRow(column, LocKeys.MenuLanguage);
            _languageButtons = new[]
            {
                UiFactory.CreateButton("Language_En", languageRow, s, s.MenuButtonSize, s.ButtonColor, s.MediumFontSize,
                    Loc.Get(LocKeys.LanguageEn), () => Loc.SetLanguage(Language.En), out _),
                UiFactory.CreateButton("Language_Ru", languageRow, s, s.MenuButtonSize, s.ButtonColor, s.MediumFontSize,
                    Loc.Get(LocKeys.LanguageRu), () => Loc.SetLanguage(Language.Ru), out _)
            };

            PlayButton = UiFactory.CreateButton("PlayButton", column, s, s.PlayButtonSize, s.PrimaryButtonColor, s.LargeFontSize,
                Loc.Get(LocKeys.MenuPlay), Play, out _);

            Text version = UiFactory.CreateText("Version", _canvas.transform, s, s.SmallFontSize, TextAnchor.LowerLeft,
                Loc.Get(LocKeys.MenuVersion) + " " + Application.version);
            RectTransform vrt = version.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.zero;
            vrt.pivot = Vector2.zero;
            vrt.anchoredPosition = s.VersionMargin;
            vrt.sizeDelta = s.MenuButtonSize;

            RefreshSelection();
        }

        private RectTransform CreateLabeledRow(Transform column, string labelKey)
        {
            UiStyle s = _style;
            RectTransform row = UiFactory.CreateRow("Row_" + labelKey, column, s.MenuItemSpacing, s.MenuButtonSize.y);
            UiFactory.CreateLayoutText("Label", row, s, s.MediumFontSize, s.MenuButtonSize, Loc.Get(labelKey), TextAnchor.MiddleRight);
            return row;
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < _fighterButtons.Length; i++)
                Highlight(_fighterButtons[i], string.Equals(_database.Fighters[i].Id, _fighterId, StringComparison.Ordinal));
            for (int i = 0; i < _botButtons.Length; i++) Highlight(_botButtons[i], BotChoices[i] == _bots);
            for (int i = 0; i < _languageButtons.Length; i++) Highlight(_languageButtons[i], (int)Loc.Current == i);
        }

        private void Highlight(Button button, bool selected)
        {
            if (button == null) return;
            ((Image)button.targetGraphic).color = selected ? _style.ButtonSelectedColor : _style.ButtonColor;
        }
    }
}
