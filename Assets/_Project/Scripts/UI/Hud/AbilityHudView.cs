using Arena.UI.Style;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>
    /// Shows <see cref="AbilityHudInfo"/> on the HUD controls (GDD §5.1, Art-Spec §4.1): the ult button is bright only when the
    /// ult is charged; the thin ring around it fills clockwise with the charge and turns gold (full) with a soft glow when it is
    /// ready (A.7c, replaces the A.7a petals); the attack stick gets a light accent while held; the trick button and its icon dim while not ready, acorn beads show the
    /// uses left (no text). Writes to uGUI only when a value changes (no canvas rebuild every frame, no allocations after the
    /// beads exist).
    /// </summary>
    public sealed class AbilityHudView
    {
        private readonly MatchHudControls _controls;
        private readonly UiStyle _style;

        private bool _hasShown;
        private AbilityHudInfo _shown;
        private bool _attackShown;
        private bool _attackHeldShown;

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
                SetColor(_controls.UltIcon, info.UltCharged ? _style.IconReadyColor : _style.IconNotReadyColor);
                if (_controls.UltGlow != null && _controls.UltGlow.enabled != info.UltCharged) _controls.UltGlow.enabled = info.UltCharged;
            }
            if (!_hasShown || info.UltCharge != _shown.UltCharge || info.HasUlt != _shown.HasUlt || info.UltCharged != _shown.UltCharged)
                ShowRing(info.HasUlt ? info.UltCharge : 0f, info.UltCharged);

            if (!_hasShown || info.TrickReady != _shown.TrickReady)
            {
                SetColor(_controls.TrickImage, info.TrickReady ? _style.TrickButtonColor : _style.TrickButtonDisabledColor);
                SetColor(_controls.TrickIcon, info.TrickReady ? _style.IconReadyColor : _style.IconNotReadyColor);
            }
            if (!_hasShown || info.TrickUsesLeft != _shown.TrickUsesLeft)
            {
                Image[] dots = _controls.TrickDots;
                for (int i = 0; i < dots.Length; i++)
                    SetColor(dots[i], i < info.TrickUsesLeft ? _style.TrickDotOnColor : _style.TrickDotOffColor);
            }

            _shown = info;
            _hasShown = true;
        }

        /// <summary>Ring fill = charge (charging colour); a charged ult shows the full ring in the ready colour.</summary>
        private void ShowRing(float charge, bool charged)
        {
            Image fill = _controls.UltRingFill;
            if (fill == null) return;
            float amount = charged ? 1f : Mathf.Clamp01(charge);
            if (fill.fillAmount != amount) fill.fillAmount = amount;
            SetColor(fill, charged ? _style.UltStick.BaseColor : _style.UltRingFillColor);
        }

        /// <summary>Light accent of the attack stick while it is pressed / aiming (A.7c). Call every frame; writes only on change.</summary>
        public void ShowAttackHeld(bool held)
        {
            if (_attackHeldShown == held && _attackShown) return;
            _attackShown = true;
            _attackHeldShown = held;
            SetColor(_controls.AttackBase, held ? _style.AttackHeldBaseColor : _style.AttackStick.BaseColor);
            SetColor(_controls.AttackKnob, held ? _style.AttackHeldKnobColor : _style.AttackStick.KnobColor);
        }

        /// <summary>
        /// Glow of the charged ult (GDD §5.1: the button lights up): the ult ring (with the button) and the golden glow behind it
        /// pulse in size while charged and return to normal otherwise. Call every frame.
        /// </summary>
        /// <param name="time">Unscaled time, seconds.</param>
        /// <param name="frequency">Pulses per second.</param>
        /// <param name="extraScale">Extra scale at the peak (0.08 = +8 %).</param>
        public void AnimateUltGlow(bool charged, float time, float frequency, float extraScale)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * frequency * Mathf.PI * 2f);
            float k = charged ? 1f + extraScale * pulse : 1f;
            if (_controls.UltBase != null) SetScale(_controls.UltBase.rectTransform, k);
            if (_controls.UltGlow != null) SetScale(_controls.UltGlow.rectTransform, k);
        }

        private static void SetScale(Transform t, float k)
        {
            if (t.localScale.x != k) t.localScale = new Vector3(k, k, 1f);
        }

        private static void SetColor(Graphic graphic, Color color)
        {
            if (graphic != null && graphic.color != color) graphic.color = color;
        }
    }
}
