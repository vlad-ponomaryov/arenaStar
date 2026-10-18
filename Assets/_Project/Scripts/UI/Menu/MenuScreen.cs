using System;
using Arena.Bots;
using Arena.Core.Modes;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Net.Client;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Settings;
using Arena.Online;
using Arena.UI.Common;
using Arena.UI.Settings;
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
    /// "Settings" opens the <see cref="SettingsPanel"/> (S2.10).
    /// S3.11 (Server-Spec §12.4): "Play 3 vs 3" (Acorns online), "Last Beast" (online) and "Training" (offline with bots — the
    /// bot difficulty applies to it) with the chosen fighter; the region button (cycles the regions of <c>NetConfig</c>) with its
    /// ping coloured like the badge (the session pings the regions when the menu opens and picks the best one); a status line
    /// shows the error of a failed search. S3.14: the "Training mode" row picks Acorns or Last Beast for "Training". While searching, the <see cref="MatchmakingScreen"/> covers the menu; "Cancel" stops
    /// the search; <c>MatchStart</c> loads the match. After "Play again" of an online match the menu opens with the search running.
    /// </summary>
    public sealed class MenuScreen : MonoBehaviour
    {
        private static readonly BotDifficulty[] BotChoices = { BotDifficulty.Novice, BotDifficulty.Easy };
        private static readonly ModeKind[] TrainingModes = { ModeKind.Acorns, ModeKind.LastBeast };

        [SerializeField] private GameDatabase _database;
        [SerializeField] private UiStyle _style;

        private Canvas _canvas;
        private string _fighterId;
        private BotDifficulty _bots;
        private Button[] _fighterButtons = new Button[0];
        private Button[] _botButtons = new Button[0];
        private Button[] _trainingModeButtons = new Button[0];
        private ModeKind _trainingMode;
        private Button[] _languageButtons = new Button[0];
        private GameObject _matchmakingCanvas;

        public GameDatabase Database => _database;
        public UiStyle Style => _style;
        public string SelectedFighterId => _fighterId;
        public BotDifficulty SelectedBots => _bots;

        /// <summary>Mode of the offline "Training" (S3.14).</summary>
        public ModeKind SelectedTrainingMode => _trainingMode;
        /// <summary>"Training": an offline match with bots.</summary>
        public Button TrainingButton { get; private set; }
        public Button SettingsButton { get; private set; }

        /// <summary>"Play 3 vs 3": an online Acorns search.</summary>
        public Button PlayOnlineButton { get; private set; }

        /// <summary>"Last Beast": an online free-for-all search.</summary>
        public Button LastBeastButton { get; private set; }

        /// <summary>"Back to the match": shown while a ticket of an unfinished match is saved (S3.12, Server-Spec §7).</summary>
        public Button RejoinButton { get; private set; }

        public Button RegionButton { get; private set; }
        public Text RegionPing { get; private set; }
        public Text OnlineStatus { get; private set; }
        public MatchmakingScreen Matchmaking { get; private set; }

        private Text _regionLabel;
        private OnlinePhase _shownPhase;
        private OnlineFailure _shownFailure;
        private bool _shownAvailable;
        private bool _shownRejoin;
        private int _shownRegion = -1;
        private int _shownPingMs = int.MinValue;
        private bool _onlineDirty = true;
        public SettingsPanel Settings { get; private set; }

        private void Start()
        {
            if (_database == null || _style == null)
            {
                Debug.LogError("MenuScreen: database or style is not set", this);
                enabled = false;
                return;
            }
            if (_database.Strings != null) Loc.Init(_database.Strings);
            FrameRateApplier.Apply(PlayerSettingsStore.SharedFor(_database.Feel).Current, _database.Feel);
            MatchLaunchParams last = SceneFlow.Current;
            _fighterId = last != null ? last.FighterId : DefaultFighterId();
            _bots = last != null ? last.Bots : BotChoices[0];
            _trainingMode = last != null && !last.Online ? last.Mode : TrainingModes[0];
            Loc.LanguageChanged += Rebuild;
            Rebuild();
            OnlineSession online = OnlineSession.Instance;
            if (online != null && online.IsAvailable && !online.IsBusy) online.RefreshPings();
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

        public void SelectTrainingMode(ModeKind mode)
        {
            _trainingMode = mode;
            RefreshSelection();
        }

        /// <summary>"Training": an offline match with the current choice (fighter, bots, mode) and a new seed.</summary>
        public void Play()
        {
            OnlineSession online = OnlineSession.Instance;
            if (online != null && online.IsBusy) online.Cancel();
            SceneFlow.PlayMatch(new MatchLaunchParams(_fighterId, _bots, Environment.TickCount, false, _trainingMode));
        }

        /// <summary>Starts an online search of <paramref name="mode"/> with the current fighter in the selected region.</summary>
        public void PlayOnline(ModeKind mode)
        {
            OnlineSession online = OnlineSession.Instance;
            _onlineDirty = true;
            if (online == null || !online.IsAvailable || online.IsBusy) return;
            online.Search(mode, _fighterId, _bots);
        }

        /// <summary>Stops the running search (the matchmaking screen's "Cancel").</summary>
        public void CancelSearch()
        {
            OnlineSession online = OnlineSession.Instance;
            _onlineDirty = true;
            if (online != null && online.IsBusy) online.Cancel();
        }

        /// <summary>"Back to the match": returns to the saved unfinished match.</summary>
        public void Rejoin()
        {
            OnlineSession online = OnlineSession.Instance;
            _onlineDirty = true;
            if (online != null && online.CanRejoin) online.Rejoin();
        }

        /// <summary>Picks the next region of the config (the region button).</summary>
        public void NextRegion()
        {
            OnlineSession online = OnlineSession.Instance;
            if (online == null || online.RegionCount == 0) return;
            online.SelectRegion((online.RegionIndex + 1) % online.RegionCount);
        }

        private void Update()
        {
            if (OnlineStatus == null) return;
            OnlineSession online = OnlineSession.Instance;
            Matchmaking.Tick(online);
            bool available = online != null && online.IsAvailable;
            RefreshRegion(online, available);
            OnlinePhase phase = online != null ? online.Phase : OnlinePhase.Idle;
            OnlineFailure failure = online != null ? online.Failure : OnlineFailure.None;
            bool rejoin = available && online.CanRejoin;
            if (!_onlineDirty && available == _shownAvailable && phase == _shownPhase && failure == _shownFailure
                && rejoin == _shownRejoin) return;
            _onlineDirty = false;
            _shownAvailable = available;
            _shownRejoin = rejoin;
            RejoinButton.gameObject.SetActive(rejoin);
            _shownPhase = phase;
            _shownFailure = failure;
            PlayOnlineButton.interactable = available;
            LastBeastButton.interactable = available;
            RegionButton.interactable = available;
            string key = !available ? LocKeys.ErrorServerUnreachable : phase == OnlinePhase.Failed ? FailureKey(failure) : null;
            OnlineStatus.text = key != null ? Loc.Get(key) : string.Empty;
        }

        private void RefreshRegion(OnlineSession online, bool available)
        {
            int region = available ? online.RegionIndex : -1;
            int ping = available ? online.SelectedPingMs : -1;
            if (region == _shownRegion && ping == _shownPingMs) return;
            _shownRegion = region;
            _shownPingMs = ping;
            NetRegionConfig r = available ? online.SelectedRegion : null;
            _regionLabel.text = r != null ? Loc.Get(r.NameKey) : "-";
            // On a change only (a few times per menu visit), not a per-frame allocation.
            RegionPing.text = ping >= 0 ? NumberStringCache.Get(ping) + " " + Loc.Get(LocKeys.HudPingMs) : "-";
            RegionPing.color = _style.Ping(available ? online.SelectedPingQuality : PingQuality.Unknown);
        }

        /// <summary>Error text of a failed search (§12.4).</summary>
        public static string FailureKey(OnlineFailure failure)
        {
            switch (failure)
            {
                case OnlineFailure.UpdateRequired: return LocKeys.ErrorUpdateRequired;
                case OnlineFailure.Maintenance: return LocKeys.ErrorMaintenance;
                case OnlineFailure.UdpBlocked: return LocKeys.ErrorUdpBlocked;
                case OnlineFailure.MatchOver: return LocKeys.ErrorMatchOver;
                default: return LocKeys.ErrorServerUnreachable;
            }
        }

        private string DefaultFighterId()
        {
            MatchRunnerConfig runner = _database.MatchRunner;
            if (runner != null && runner.LocalSlot >= 0 && runner.LocalSlot < runner.SlotFighterIds.Count)
                return runner.SlotFighterIds[runner.LocalSlot];
            return _database.Fighters.Count > 0 ? _database.Fighters[0].Id : null;
        }

        public void OpenSettings()
        {
            if (Settings != null) Settings.Show();
        }

        private void Rebuild()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (Settings != null) Destroy(Settings.gameObject);
            if (_matchmakingCanvas != null) Destroy(_matchmakingCanvas);
            UiStyle s = _style;
            _canvas = UiFactory.CreateCanvas("MenuCanvas", transform, s, 0);
            RectTransform column = UiFactory.CreateColumn("Column", _canvas.transform, s.MenuRowSpacing);
            UiFactory.Stretch(column);

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

            _trainingModeButtons = new Button[TrainingModes.Length];
            RectTransform trainingRow = CreateLabeledRow(column, LocKeys.MenuTrainingMode);
            for (int i = 0; i < TrainingModes.Length; i++)
            {
                ModeKind mode = TrainingModes[i];
                _trainingModeButtons[i] = UiFactory.CreateButton("TrainingMode_" + mode, trainingRow, s,
                    new Vector2(s.MenuButtonSize.x * 1.5f, s.MenuButtonSize.y), s.ButtonColor, s.MediumFontSize,
                    Loc.Get(mode == ModeKind.LastBeast ? LocKeys.ModeLastBeast : LocKeys.ModeAcorns), () => SelectTrainingMode(mode), out _);
            }

            RectTransform languageRow = CreateLabeledRow(column, LocKeys.MenuLanguage);
            _languageButtons = new[]
            {
                UiFactory.CreateButton("Language_En", languageRow, s, s.MenuButtonSize, s.ButtonColor, s.MediumFontSize,
                    Loc.Get(LocKeys.LanguageEn), () => Loc.SetLanguage(Language.En), out _),
                UiFactory.CreateButton("Language_Ru", languageRow, s, s.MenuButtonSize, s.ButtonColor, s.MediumFontSize,
                    Loc.Get(LocKeys.LanguageRu), () => Loc.SetLanguage(Language.Ru), out _)
            };

            RejoinButton = UiFactory.CreateButton("RejoinButton", column, s, s.PlayButtonSize, s.PrimaryButtonColor, s.LargeFontSize,
                Loc.Get(LocKeys.NetRejoin), Rejoin, out _);
            RejoinButton.gameObject.SetActive(false);
            _shownRejoin = false;

            RectTransform playRow = UiFactory.CreateRow("PlayRow", column, s.MenuItemSpacing, s.PlayButtonSize.y);
            PlayOnlineButton = UiFactory.CreateButton("PlayOnlineButton", playRow, s, s.PlayButtonSize, s.PrimaryButtonColor,
                s.LargeFontSize, Loc.Get(LocKeys.MenuPlayOnline), () => PlayOnline(ModeKind.Acorns), out _);
            LastBeastButton = UiFactory.CreateButton("LastBeastButton", playRow, s, s.PlayButtonSize, s.ButtonColor, s.LargeFontSize,
                Loc.Get(LocKeys.MenuLastBeast), () => PlayOnline(ModeKind.LastBeast), out _);
            TrainingButton = UiFactory.CreateButton("TrainingButton", playRow, s, s.PlayButtonSize, s.ButtonColor, s.LargeFontSize,
                Loc.Get(LocKeys.MenuTraining), Play, out _);

            RectTransform regionRow = CreateLabeledRow(column, LocKeys.MenuRegion);
            RegionButton = UiFactory.CreateButton("RegionButton", regionRow, s, new Vector2(s.MenuButtonSize.x * 2f, s.MenuButtonSize.y),
                s.ButtonColor, s.MediumFontSize, string.Empty, NextRegion, out _regionLabel);
            RegionPing = UiFactory.CreateLayoutText("RegionPing", regionRow, s, s.MediumFontSize, s.MenuButtonSize, "-");
            OnlineStatus = UiFactory.CreateLayoutText("OnlineStatus", column, s, s.SmallFontSize,
                new Vector2(s.PlayButtonSize.x * 3f, s.MenuButtonSize.y * 0.5f), string.Empty);
            _shownRegion = -1;
            _shownPingMs = int.MinValue;
            _onlineDirty = true;
            SettingsButton = UiFactory.CreateButton("SettingsButton", column, s, s.MenuButtonSize, s.ButtonColor, s.MediumFontSize,
                Loc.Get(LocKeys.MenuSettings), OpenSettings, out _);
            Settings = SettingsPanel.Create(transform, s, _database.Feel, PlayerSettingsStore.SharedFor(_database.Feel), null);
            Matchmaking = MatchmakingScreen.Build(transform, s, CancelSearch);
            _matchmakingCanvas = Matchmaking.Canvas.gameObject;

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
            for (int i = 0; i < _trainingModeButtons.Length; i++) Highlight(_trainingModeButtons[i], TrainingModes[i] == _trainingMode);
            for (int i = 0; i < _languageButtons.Length; i++) Highlight(_languageButtons[i], (int)Loc.Current == i);
        }

        private void Highlight(Button button, bool selected)
        {
            if (button == null) return;
            ((Image)button.targetGraphic).color = selected ? _style.ButtonSelectedColor : _style.ButtonColor;
        }
    }
}
