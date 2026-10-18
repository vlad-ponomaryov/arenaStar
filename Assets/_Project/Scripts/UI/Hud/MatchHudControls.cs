using Arena.UI.Controls;
using UnityEngine;

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
    }
}
