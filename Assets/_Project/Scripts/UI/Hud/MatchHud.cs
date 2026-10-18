using Arena.Core.Math;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Settings;
using Arena.Gameplay.Input;
using Arena.Gameplay.Match;
using Arena.UI.Controls;
using Arena.UI.Results;
using Arena.UI.Style;
using UnityEngine;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Scene bootstrap of the match HUD: builds the controls from <see cref="UiStyle"/> and every frame forwards the sticks
    /// into the runner's <see cref="LocalInputCollector"/> (touch channel, view space). Runs before <see cref="MatchRunner"/>
    /// so the values of this frame reach this frame's ticks. Lives in Arena.UI because UI depends on Gameplay, not back.
    /// The ult stick works only while the ult is charged (GDD §5.1); ability state is shown by <see cref="AbilityHudView"/>.
    /// The Acorns score bar (<see cref="ScoreBarView"/>) is on the same canvas; when the match ends the controls are hidden
    /// and the <see cref="ResultsScreen"/> appears ("Play again" → <see cref="SceneFlow.Restart"/>, "Menu" → <see cref="SceneFlow.ToMenu"/>).
    /// S2.10: the layout follows the "mirrored" setting, the charged ult stick pulses (<see cref="FeelConfig"/>), an FPS counter
    /// sits in the top-left corner; the aim line knows whether the ult stick is held (its range).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MatchHud : MonoBehaviour
    {
        [SerializeField] private MatchRunner _runner;
        [SerializeField] private UiStyle _style;

        private MatchHudControls _controls;
        private AbilityHudView _abilityView;
        private ScoreBarView _scoreBar;
        private ResultsScreen _results;
        private FpsCounter _fps;
        private FeelConfig _feel;

        public MatchRunner Runner => _runner;
        public UiStyle Style => _style;
        public MatchHudControls Controls => _controls;
        public ScoreBarView ScoreBar => _scoreBar;
        public ResultsScreen Results => _results;
        public FpsCounter Fps => _fps;

        private void Start()
        {
            if (_runner == null || _style == null)
            {
                Debug.LogError("MatchHud: runner or style is not set", this);
                enabled = false;
                return;
            }
            if (_runner.Database != null && _runner.Database.Strings != null) Loc.Init(_runner.Database.Strings);
            _feel = _runner.Database != null ? _runner.Database.Feel : null;
            _controls = MatchHudBuilder.Build(transform, _style, PlayerSettingsStore.SharedFor(_feel).Current.MirroredLayout);
            _abilityView = new AbilityHudView(_controls, _style);
            _scoreBar = ScoreBarView.Build(_controls.Canvas.transform, _style);
            _results = ResultsScreen.Build(transform, _style, SceneFlow.Restart, SceneFlow.ToMenu);
            if (_feel != null) _fps = new FpsCounter(_controls.Canvas.transform, _style, _feel.FpsUpdateSeconds);
        }

        private void Update()
        {
            if (_controls == null) return;
            _fps?.Tick(Time.unscaledDeltaTime);
            IMatchDriver driver = _runner.Driver;
            if (driver != null) _scoreBar.Apply(driver.Defs, driver.State, _runner.LocalTeam);
            if (_runner.IsMatchEnded)
            {
                if (!_results.IsShown)
                {
                    SetControlsVisible(false);
                    _results.Show(driver.Defs, driver.State, _runner.LocalTeam);
                }
                _results.Tick();
                return;
            }
            LocalInputCollector input = _runner.InputCollector;
            AbilityHudInfo abilities = driver != null ? AbilityHudInfo.From(driver.Defs, driver.State, _runner.LocalSlot) : default;
            if (driver != null) _abilityView.Apply(in abilities);
            bool ultActive = abilities.UltCharged;
            if (_feel != null) _abilityView.AnimateUltGlow(ultActive, Time.unscaledTime, _feel.UltGlowFrequency, _feel.UltGlowScale);

            input.SetMove(InputChannel.Touch, ToVec2(_controls.MoveStick.Value));

            bool aimingAttack = _controls.AttackStick.IsHeld;
            Vector2 aim = aimingAttack ? _controls.AttackStick.Aim
                : ultActive && _controls.UltStick.IsHeld ? _controls.UltStick.Aim : Vector2.zero;
            input.SetAim(InputChannel.Touch, ToVec2(aim), !aimingAttack);

            if (_controls.AttackStick.TryConsumeRelease(out StickGestureResult attack, out Vector2 attackAim))
            {
                if (attack == StickGestureResult.Tap) input.PressAttack(Vec2.Zero, true);
                else if (attack == StickGestureResult.Manual) input.PressAttack(ToVec2(attackAim), false);
            }
            if (_controls.UltStick.TryConsumeRelease(out StickGestureResult ult, out Vector2 ultAim) && ultActive)
            {
                if (ult == StickGestureResult.Tap) input.PressUlt(Vec2.Zero, true);
                else if (ult == StickGestureResult.Manual) input.PressUlt(ToVec2(ultAim), false);
            }
            if (_controls.TrickButton.ConsumePress()) input.PressTrick();
        }

        private void SetControlsVisible(bool visible)
        {
            _controls.MoveStick.gameObject.SetActive(visible);
            _controls.AttackStick.gameObject.SetActive(visible);
            _controls.UltStick.gameObject.SetActive(visible);
            _controls.TrickButton.gameObject.SetActive(visible);
        }

        private static Vec2 ToVec2(Vector2 v) => new Vec2(v.x, v.y);
    }
}
