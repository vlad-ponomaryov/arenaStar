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

        /// <summary>Base and knob images of the ult stick (recoloured by charge state).</summary>
        public Image UltBase;
        public Image UltKnob;

        /// <summary>Petals of the ult "flower" around the ult stick (A.7a): they fill one by one with the charge.</summary>
        public Image[] UltPetals;

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
