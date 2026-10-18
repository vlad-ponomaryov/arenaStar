using Arena.Core.Math;
using Arena.Gameplay.Input;
using Arena.Gameplay.Match;
using Arena.UI.Controls;
using Arena.UI.Style;
using UnityEngine;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Scene bootstrap of the match HUD: builds the controls from <see cref="UiStyle"/> and every frame forwards the sticks
    /// into the runner's <see cref="LocalInputCollector"/> (touch channel, view space). Runs before <see cref="MatchRunner"/>
    /// so the values of this frame reach this frame's ticks. Lives in Arena.UI because UI depends on Gameplay, not back.
    /// The ult stick works only while the ult is charged (GDD §5.1); ability state is shown by <see cref="AbilityHudView"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MatchHud : MonoBehaviour
    {
        [SerializeField] private MatchRunner _runner;
        [SerializeField] private UiStyle _style;

        private MatchHudControls _controls;
        private AbilityHudView _abilityView;

        public MatchRunner Runner => _runner;
        public UiStyle Style => _style;
        public MatchHudControls Controls => _controls;

        private void Start()
        {
            if (_runner == null || _style == null)
            {
                Debug.LogError("MatchHud: runner or style is not set", this);
                enabled = false;
                return;
            }
            _controls = MatchHudBuilder.Build(transform, _style);
            _abilityView = new AbilityHudView(_controls, _style);
        }

        private void Update()
        {
            if (_controls == null) return;
            LocalInputCollector input = _runner.InputCollector;
            AbilityHudInfo abilities = AbilityHudInfo.From(_runner.Simulation, _runner.LocalSlot);
            if (_runner.Simulation != null) _abilityView.Apply(in abilities);
            bool ultActive = abilities.UltCharged;

            input.SetMove(InputChannel.Touch, ToVec2(_controls.MoveStick.Value));

            Vector2 aim = _controls.AttackStick.IsHeld ? _controls.AttackStick.Aim
                : ultActive && _controls.UltStick.IsHeld ? _controls.UltStick.Aim : Vector2.zero;
            input.SetAim(InputChannel.Touch, ToVec2(aim));

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

        private static Vec2 ToVec2(Vector2 v) => new Vec2(v.x, v.y);
    }
}
