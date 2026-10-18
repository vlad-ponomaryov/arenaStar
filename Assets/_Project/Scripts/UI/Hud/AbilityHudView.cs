using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Shows <see cref="AbilityHudInfo"/> on the HUD controls (GDD §5.1): the ult stick is bright only when the ult is
    /// charged, a radial ring shows the charge; the trick button dims while not ready, dots show the uses left (no text).
    /// Writes to uGUI only when a value changes (no canvas rebuild every frame, no allocations after the dots exist).
    /// </summary>
    public sealed class AbilityHudView
    {
        private readonly MatchHudControls _controls;
        private readonly UiStyle _style;

        private bool _hasShown;
        private AbilityHudInfo _shown;

        public AbilityHudView(MatchHudControls controls, UiStyle style)
        {
            _controls = controls;
            _style = style;
        }

        public void Apply(in AbilityHudInfo info)
        {
            if (_controls.TrickDots == null || _controls.TrickDots.Length != info.TrickMaxUses)
            {
                MatchHudBuilder.CreateTrickDots(_controls, _style, info.TrickMaxUses);
                _hasShown = false;
            }

            if (!_hasShown || info.UltCharged != _shown.UltCharged)
            {
                SetColor(_controls.UltBase, info.UltCharged ? _style.UltStick.BaseColor : _style.UltNotReadyBaseColor);
                SetColor(_controls.UltKnob, info.UltCharged ? _style.UltStick.KnobColor : _style.UltNotReadyKnobColor);
            }
            if (_controls.UltChargeRing != null && (!_hasShown || info.UltCharge != _shown.UltCharge || info.HasUlt != _shown.HasUlt))
            {
                _controls.UltChargeRing.fillAmount = info.HasUlt ? info.UltCharge : 0f;
            }

            if (!_hasShown || info.TrickReady != _shown.TrickReady)
                SetColor(_controls.TrickImage, info.TrickReady ? _style.TrickButtonColor : _style.TrickButtonDisabledColor);
            if (!_hasShown || info.TrickUsesLeft != _shown.TrickUsesLeft)
            {
                Image[] dots = _controls.TrickDots;
                for (int i = 0; i < dots.Length; i++)
                    SetColor(dots[i], i < info.TrickUsesLeft ? _style.TrickDotOnColor : _style.TrickDotOffColor);
            }

            _shown = info;
            _hasShown = true;
        }

        /// <summary>
        /// Glow of the charged ult (GDD §5.1: the button lights up): the ult stick base pulses in size while charged and returns
        /// to normal otherwise. Call every frame.
        /// </summary>
        /// <param name="time">Unscaled time, seconds.</param>
        /// <param name="frequency">Pulses per second.</param>
        /// <param name="extraScale">Extra scale at the peak (0.08 = +8 %).</param>
        public void AnimateUltGlow(bool charged, float time, float frequency, float extraScale)
        {
            if (_controls.UltBase == null) return;
            float k = charged ? 1f + extraScale * (0.5f + 0.5f * Mathf.Sin(time * frequency * Mathf.PI * 2f)) : 1f;
            Transform t = _controls.UltBase.rectTransform;
            if (t.localScale.x != k) t.localScale = new Vector3(k, k, 1f);
        }

        private static void SetColor(Graphic graphic, Color color)
        {
            if (graphic != null && graphic.color != color) graphic.color = color;
        }
    }
}
