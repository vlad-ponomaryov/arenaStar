using Arena.UI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace Arena.UI.Hud
{
    /// <summary>References to the controls created by <see cref="MatchHudBuilder"/>.</summary>
    public sealed class MatchHudControls
    {
        public Canvas Canvas;
        public VirtualStick MoveStick;
        public VirtualStick AttackStick;
        public VirtualStick UltStick;
        public TapButton TrickButton;

        /// <summary>Base and knob images of the attack stick (light, translucent; accented while held, A.7c).</summary>
        public Image AttackBase;
        public Image AttackKnob;

        /// <summary>Ult stick (A.7c): the base is the thin charge ring track, the knob is the round ult button (recoloured by
        /// charge state).</summary>
        public Image UltBase;
        public Image UltKnob;

        /// <summary>Radial fill of the ult charge ring (child of the base, under the button): fillAmount = charge.</summary>
        public Image UltRingFill;

        /// <summary>Golden glow behind the ult while it is charged.</summary>
        public Image UltGlow;

        /// <summary>Ult icon on the ult knob.</summary>
        public Image UltIcon;

        public Image TrickImage;

        /// <summary>Trick icon on the trick button.</summary>
        public Image TrickIcon;

        /// <summary>Row of acorn beads above the trick button (one per use), created on the first frame the rules are known.</summary>
        public Image[] TrickDots;
    }
}
