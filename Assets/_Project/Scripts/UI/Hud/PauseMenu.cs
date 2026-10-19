using System;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Gameplay.Settings;
using Arena.UI.Common;
using Arena.UI.Settings;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Match menu (D-129): a pause button in the top-right corner of the HUD (clear of the sticks) opens a panel —
    /// "Resume", "Settings" (the <see cref="SettingsPanel"/> of the menu; most choices apply from the next match), "Leave match"
    /// with a confirmation ("Leave" / "Stay"). Offline the panel pauses the match (<c>onPause(true/false)</c>); online there is
    /// no pause — the title is "Menu" with "The battle goes on", leaving is the same scene change as the connection banner
    /// (<c>onLeave</c> → <c>SceneFlow.ToMenu</c> → Leave, D-116). <see cref="Back"/> is the Android Back key / Escape: it closes
    /// the innermost layer, or opens the menu. Built in code from <see cref="UiStyle"/> (A.6 skin); no allocations per frame.
    /// </summary>
    public sealed class PauseMenu
    {
        /// <summary>Above the HUD and its banners, below the settings panel (10).</summary>
        public const int SortingOrder = 8;

        private readonly GameObject _root;
        private readonly GameObject _mainPage;
        private readonly GameObject _confirmPage;
        private readonly SettingsPanel _settings;
        private readonly Action<bool> _onPause;
        private readonly Action _onLeave;
        private bool _left;

        public Button PauseButton { get; }
        public Button ResumeButton { get; }
        public Button SettingsButton { get; }
        public Button LeaveButton { get; }
        public Button LeaveYesButton { get; }
        public Button LeaveNoButton { get; }
        public Text Title { get; }
        public SettingsPanel Settings => _settings;

        /// <summary>The panel is up (offline: the match is paused).</summary>
        public bool IsOpen => _root.activeSelf;

        /// <summary>The "Leave the match?" question is shown.</summary>
        public bool IsConfirming => IsOpen && _confirmPage.activeSelf;

        public bool IsSettingsOpen => _settings != null && _settings.IsShown;

        private PauseMenu(GameObject root, GameObject mainPage, GameObject confirmPage, SettingsPanel settings, Button pause,
            Button resume, Button settingsButton, Button leave, Button yes, Button no, Text title, Action<bool> onPause,
            Action onLeave)
        {
            _root = root;
            _mainPage = mainPage;
            _confirmPage = confirmPage;
            _settings = settings;
            PauseButton = pause;
            ResumeButton = resume;
            SettingsButton = settingsButton;
            LeaveButton = leave;
            LeaveYesButton = yes;
            LeaveNoButton = no;
            Title = title;
            _onPause = onPause;
            _onLeave = onLeave;
        }

        /// <param name="hudCanvas">Canvas of the match controls: the pause button goes there.</param>
        /// <param name="parent">Parent of the panel canvas and the settings canvas.</param>
        /// <param name="online">Online match: no pause, the title says "Menu" and the battle goes on.</param>
        /// <param name="feel">Vibration steps of the settings panel (may be null).</param>
        /// <param name="onPause">Offline: pause (true) / resume (false) the match; also called online (the HUD hides the sticks).</param>
        /// <param name="onLeave">Confirmed "Leave": go to the menu.</param>
        public static PauseMenu Build(Transform hudCanvas, Transform parent, UiStyle style, bool online, FeelConfig feel,
            Action<bool> onPause, Action onLeave)
        {
            if (style == null) throw new ArgumentNullException(nameof(style));
            Button pause = CreatePauseButton(hudCanvas, style);

            Canvas canvas = UiFactory.CreateCanvas("PauseMenuCanvas", parent, style, SortingOrder);
            UiFactory.CreateBackdrop(canvas.transform, style, style.SettingsPanelColor, false);
            RectTransform outer = UiFactory.CreateColumn("Outer", canvas.transform, 0f);
            UiFactory.Stretch(outer);
            RectTransform panel = UiFactory.CreatePanel("Panel", outer, style, style.MenuRowSpacing, true);
            Text title = UiFactory.CreatePlankTitle("Title", panel, style, style.PauseMenuWidth, style.LargeFontSize,
                Loc.Get(online ? LocKeys.PauseMenuTitle : LocKeys.PauseTitle));
            var line = new Vector2(style.PauseMenuWidth, style.MediumFontSize * 1.6f);

            RectTransform main = UiFactory.CreateColumn("Main", panel, style.MenuRowSpacing);
            if (online) UiFactory.CreatePanelText("OnlineHint", main, style, style.MediumFontSize, line, Loc.Get(LocKeys.PauseOnlineHint));
            Button resume = UiFactory.CreateButton("ResumeButton", main, style, style.PauseMenuButtonSize, style.PrimaryButtonColor,
                style.LargeFontSize, Loc.Get(LocKeys.PauseResume), null, out _);
            Button settingsButton = UiFactory.CreateButton("SettingsButton", main, style, style.PauseMenuButtonSize, style.ButtonColor,
                style.LargeFontSize, Loc.Get(LocKeys.MenuSettings), null, out _);
            Button leave = UiFactory.CreateButton("LeaveButton", main, style, style.PauseMenuButtonSize, style.SecondaryButtonColor,
                style.LargeFontSize, Loc.Get(LocKeys.PauseLeave), null, out _);

            RectTransform confirm = UiFactory.CreateColumn("Confirm", panel, style.MenuRowSpacing);
            UiFactory.CreatePanelText("Question", confirm, style, style.LargeFontSize, new Vector2(style.PauseMenuWidth,
                style.LargeFontSize * 1.6f), Loc.Get(LocKeys.PauseLeaveConfirm));
            Button yes = UiFactory.CreateButton("LeaveYesButton", confirm, style, style.PauseMenuButtonSize, style.SecondaryButtonColor,
                style.LargeFontSize, Loc.Get(LocKeys.PauseLeaveYes), null, out _);
            Button no = UiFactory.CreateButton("LeaveNoButton", confirm, style, style.PauseMenuButtonSize, style.PrimaryButtonColor,
                style.LargeFontSize, Loc.Get(LocKeys.PauseLeaveNo), null, out _);
            confirm.gameObject.SetActive(false);

            SettingsPanel settings = SettingsPanel.Create(parent, style, feel, PlayerSettingsStore.SharedFor(feel), null);
            canvas.gameObject.SetActive(false);

            var menu = new PauseMenu(canvas.gameObject, main.gameObject, confirm.gameObject, settings, pause, resume, settingsButton,
                leave, yes, no, title, onPause, onLeave);
            pause.onClick.AddListener(menu.Open);
            resume.onClick.AddListener(menu.Close);
            settingsButton.onClick.AddListener(menu.OpenSettings);
            leave.onClick.AddListener(menu.AskLeave);
            yes.onClick.AddListener(menu.ConfirmLeave);
            no.onClick.AddListener(menu.CancelLeave);
            return menu;
        }

        public void Open()
        {
            if (IsOpen || _left) return;
            _mainPage.SetActive(true);
            _confirmPage.SetActive(false);
            _root.SetActive(true);
            _onPause?.Invoke(true);
        }

        /// <summary>"Resume": hides everything (also the settings) and lets the match go on.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            if (IsSettingsOpen) _settings.Hide();
            _root.SetActive(false);
            _onPause?.Invoke(false);
        }

        public void OpenSettings()
        {
            if (IsOpen && _settings != null) _settings.Show();
        }

        public void AskLeave()
        {
            if (!IsOpen) return;
            _mainPage.SetActive(false);
            _confirmPage.SetActive(true);
        }

        public void CancelLeave()
        {
            if (!IsConfirming) return;
            _confirmPage.SetActive(false);
            _mainPage.SetActive(true);
        }

        /// <summary>"Leave": once; the scene change does the rest (offline — nothing to save, online — Leave to the server).</summary>
        public void ConfirmLeave()
        {
            if (!IsConfirming || _left) return;
            _left = true;
            _onLeave?.Invoke();
        }

        /// <summary>Android Back / Escape: settings → close them; the question → back to the menu; the menu → resume; else open.</summary>
        public void Back()
        {
            if (_left) return;
            if (IsSettingsOpen) _settings.Hide();
            else if (IsConfirming) CancelLeave();
            else if (IsOpen) Close();
            else Open();
        }

        /// <summary>The pause button follows the HUD controls (hidden with the results).</summary>
        public void SetButtonVisible(bool visible)
        {
            if (PauseButton.gameObject.activeSelf != visible) PauseButton.gameObject.SetActive(visible);
        }

        /// <summary>Closes the panel without a resume callback (the results replace the HUD).</summary>
        public void Dismiss()
        {
            if (IsSettingsOpen) _settings.Hide();
            _root.SetActive(false);
        }

        private static Button CreatePauseButton(Transform hudCanvas, UiStyle style)
        {
            Button button = UiFactory.CreateButton("PauseButton", hudCanvas, style, style.PauseButtonSize, style.ButtonColor,
                style.MediumFontSize, string.Empty, null, out Text label);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;
            rt.anchoredPosition = new Vector2(-style.PauseButtonMargin.x, -style.PauseButtonMargin.y);
            // Two bars on the face (no text): the label colour already contrasts with the face.
            Transform face = label.transform.parent;
            float offset = (style.PauseIconBarSize.x + style.PauseIconGap) * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                Image bar = UiFactory.CreateImage(i == 0 ? "BarLeft" : "BarRight", face, label.color);
                RectTransform brt = bar.rectTransform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = style.PauseIconBarSize;
                brt.anchoredPosition = new Vector2(i == 0 ? -offset : offset, 0f);
            }
            return button;
        }
    }
}
