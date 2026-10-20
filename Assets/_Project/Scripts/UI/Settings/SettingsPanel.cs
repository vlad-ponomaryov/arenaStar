using System;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Gameplay.Feel;
using Arena.Gameplay.Settings;
using Arena.UI.Common;
using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Settings
{
    /// <summary>
    /// Settings screen over the menu (GDD §5.1, §5.3, D-029, D-059), built in code from <see cref="UiStyle"/>: mirrored layout
    /// (Off/On), vibration strength (the steps of <see cref="FeelConfig.VibrationSteps"/>), sound volume (the steps of
    /// <see cref="FeelConfig.SoundSteps"/>), aim magnet (Off/On), colour-blind
    /// palette (Off/On), battery saver (Off/On, 30 FPS — S2.13), "Close". Every choice is saved at once through <see cref="PlayerSettingsStore"/> (local PlayerPrefs);
    /// the match reads the settings when it starts; the frame rate is applied at once (<see cref="FrameRateApplier"/>). No text input. All texts through localization keys.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        // Above the menu canvas (sorting order 0).
        private const int SortingOrder = 10;

        private PlayerSettingsStore _store;
        private UiStyle _style;
        private FeelConfig _feel;
        private int[] _vibrationSteps = new int[0];
        private int[] _soundSteps = new int[0];
        private Action _onClose;
        private Button _mirrorOff, _mirrorOn, _magnetOff, _magnetOn, _colorOff, _colorOn, _batteryOff, _batteryOn;
        private Button[] _vibrationButtons = new Button[0];
        private Button[] _soundButtons = new Button[0];

        public bool IsShown => gameObject.activeSelf;
        public GameSettings Settings => _store.Current;
        public Button CloseButton { get; private set; }

        /// <summary>Builds the panel (hidden) under <paramref name="parent"/>.</summary>
        public static SettingsPanel Create(Transform parent, UiStyle style, FeelConfig feel, PlayerSettingsStore store, Action onClose)
        {
            if (style == null) throw new ArgumentNullException(nameof(style));
            if (store == null) throw new ArgumentNullException(nameof(store));
            Canvas canvas = UiFactory.CreateCanvas("SettingsCanvas", parent, style, SortingOrder);
            SettingsPanel panel = canvas.gameObject.AddComponent<SettingsPanel>();
            panel._store = store;
            panel._style = style;
            panel._feel = feel;
            panel._onClose = onClose;
            panel._vibrationSteps = feel != null && feel.VibrationSteps != null && feel.VibrationSteps.Length > 0
                ? feel.VibrationSteps
                : new[] { 0, GameSettings.MaxVibrationPercent };
            panel._soundSteps = feel != null && feel.SoundSteps != null && feel.SoundSteps.Length > 0
                ? feel.SoundSteps
                : new[] { 0, GameSettings.MaxVibrationPercent };
            panel.Build(canvas.transform);
            canvas.gameObject.SetActive(false);
            return panel;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _onClose?.Invoke();
        }

        public void SetMirrored(bool on) => Change(s => s.MirroredLayout = on);
        public void SetVibration(int percent) => Change(s => s.VibrationPercent = percent);
        public void SetSound(int percent) => Change(s => s.SoundPercent = percent);
        public void SetAimMagnet(bool on) => Change(s => s.AimMagnet = on);
        public void SetColorblind(bool on) => Change(s => s.ColorblindPalette = on);
        public void SetBatterySaver(bool on) => Change(s => s.BatterySaver = on);

        private void Change(Action<GameSettings> edit)
        {
            GameSettings copy = _store.Current.Clone();
            edit(copy);
            _store.Save(copy);
            FrameRateApplier.Apply(_store.Current, _feel);
            Refresh();
        }

        private void Build(Transform root)
        {
            UiStyle s = _style;
            UiFactory.CreateBackdrop(root, s, s.SettingsPanelColor, false);
            // A.6: plank title on a cream panel with a wooden frame (Art-Spec §4.2).
            RectTransform outer = UiFactory.CreateColumn("Outer", root, 0f);
            UiFactory.Stretch(outer);
            RectTransform column = UiFactory.CreatePanel("Column", outer, s, s.MenuRowSpacing * 0.5f, true);
            UiFactory.CreatePlankTitle("Title", column, s, s.SettingsLabelWidth, s.LargeFontSize, Loc.Get(LocKeys.SettingsTitle));

            RectTransform mirror = CreateRow(column, LocKeys.SettingsMirror);
            _mirrorOff = Choice(mirror, "MirrorOff", Loc.Get(LocKeys.SettingsOff), () => SetMirrored(false));
            _mirrorOn = Choice(mirror, "MirrorOn", Loc.Get(LocKeys.SettingsOn), () => SetMirrored(true));

            RectTransform vibration = CreateRow(column, LocKeys.SettingsVibration);
            _vibrationButtons = new Button[_vibrationSteps.Length];
            for (int i = 0; i < _vibrationSteps.Length; i++)
            {
                int step = _vibrationSteps[i];
                _vibrationButtons[i] = Choice(vibration, "Vibration" + step,
                    step <= 0 ? Loc.Get(LocKeys.SettingsOff) : NumberStringCache.Percent(step), () => SetVibration(step));
            }

            RectTransform sound = CreateRow(column, LocKeys.SettingsSound);
            _soundButtons = new Button[_soundSteps.Length];
            for (int i = 0; i < _soundSteps.Length; i++)
            {
                int step = _soundSteps[i];
                _soundButtons[i] = Choice(sound, "Sound" + step,
                    step <= 0 ? Loc.Get(LocKeys.SettingsOff) : NumberStringCache.Percent(step), () => SetSound(step));
            }

            RectTransform magnet = CreateRow(column, LocKeys.SettingsAimMagnet);
            _magnetOff = Choice(magnet, "MagnetOff", Loc.Get(LocKeys.SettingsOff), () => SetAimMagnet(false));
            _magnetOn = Choice(magnet, "MagnetOn", Loc.Get(LocKeys.SettingsOn), () => SetAimMagnet(true));

            RectTransform color = CreateRow(column, LocKeys.SettingsColorblind);
            _colorOff = Choice(color, "ColorblindOff", Loc.Get(LocKeys.SettingsOff), () => SetColorblind(false));
            _colorOn = Choice(color, "ColorblindOn", Loc.Get(LocKeys.SettingsOn), () => SetColorblind(true));

            RectTransform battery = CreateRow(column, LocKeys.SettingsBatterySaver);
            _batteryOff = Choice(battery, "BatterySaverOff", Loc.Get(LocKeys.SettingsOff), () => SetBatterySaver(false));
            _batteryOn = Choice(battery, "BatterySaverOn", Loc.Get(LocKeys.SettingsOn), () => SetBatterySaver(true));

            CloseButton = UiFactory.CreateButton("CloseButton", column, s, s.MenuButtonSize, s.PrimaryButtonColor,
                s.MediumFontSize, Loc.Get(LocKeys.SettingsClose), Hide, out _);
        }

        private RectTransform CreateRow(Transform column, string labelKey)
        {
            UiStyle s = _style;
            RectTransform row = UiFactory.CreateRow("Row_" + labelKey, column, s.MenuItemSpacing, s.SettingsChoiceSize.y);
            int widest = Mathf.Max(2, Mathf.Max(_vibrationSteps.Length, _soundSteps.Length));
            UiFactory.AlignRowLeft(row, s.SettingsLabelWidth + widest * (s.SettingsChoiceSize.x + s.MenuItemSpacing));
            UiFactory.CreatePanelText("Label", row, s, s.MediumFontSize, new Vector2(s.SettingsLabelWidth, s.SettingsChoiceSize.y),
                Loc.Get(labelKey), TextAnchor.MiddleRight);
            return row;
        }

        private Button Choice(Transform row, string name, string label, UnityEngine.Events.UnityAction onClick) =>
            UiFactory.CreateButton(name, row, _style, _style.SettingsChoiceSize, _style.ButtonColor, _style.MediumFontSize, label,
                onClick, out _);

        private void Refresh()
        {
            GameSettings g = _store.Current;
            Highlight(_mirrorOff, !g.MirroredLayout);
            Highlight(_mirrorOn, g.MirroredLayout);
            Highlight(_magnetOff, !g.AimMagnet);
            Highlight(_magnetOn, g.AimMagnet);
            Highlight(_colorOff, !g.ColorblindPalette);
            Highlight(_colorOn, g.ColorblindPalette);
            Highlight(_batteryOff, !g.BatterySaver);
            Highlight(_batteryOn, g.BatterySaver);
            int nearest = NearestStep(_vibrationSteps, g.VibrationPercent);
            for (int i = 0; i < _vibrationButtons.Length; i++) Highlight(_vibrationButtons[i], i == nearest);
            int nearestSound = NearestStep(_soundSteps, g.SoundPercent);
            for (int i = 0; i < _soundButtons.Length; i++) Highlight(_soundButtons[i], i == nearestSound);
        }

        private static int NearestStep(int[] steps, int percent)
        {
            int best = 0;
            for (int i = 1; i < steps.Length; i++)
                if (Math.Abs(steps[i] - percent) < Math.Abs(steps[best] - percent)) best = i;
            return best;
        }

        private void Highlight(Button button, bool selected)
        {
            UiFactory.SetButtonColor(button, _style, selected ? _style.ButtonSelectedColor : _style.ButtonColor);
        }
    }
}
