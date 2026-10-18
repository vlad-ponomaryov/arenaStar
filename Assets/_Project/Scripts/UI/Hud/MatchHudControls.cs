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

        /// <summary>Radial fill behind the ult stick showing the charge.</summary>
        public Image UltChargeRing;

        public Image TrickImage;

        /// <summary>Row of dots above the trick button (one per use), created on the first frame the rules are known.</summary>
        public Image[] TrickDots;
    }
}
