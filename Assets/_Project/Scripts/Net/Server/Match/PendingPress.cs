using Arena.Core.Input;
using Arena.Core.Math;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// A button press of a late input carried over to the next tick where it makes a rising edge (Server-Spec §2.4): the button
    /// (attack, ult or trick) with the aim and <c>AutoAim</c> it was pressed with.
    /// </summary>
    public struct PendingPress
    {
        /// <summary>One of <c>Attack</c>, <c>Ult</c>, <c>Trick</c>, plus <c>AutoAim</c> for an aimed press.</summary>
        public InputButtons Buttons;
        public Vec2 Aim;

        /// <summary>The tick the press was meant for (statistics).</summary>
        public int Tick;

        /// <summary>Attack and ult carry an aim — a tick holds one of them at most (one <c>PlayerInput.Aim</c>).</summary>
        public bool IsAimed => (Buttons & (InputButtons.Attack | InputButtons.Ult)) != 0;

        public InputButtons Button => Buttons & (InputButtons.Attack | InputButtons.Ult | InputButtons.Trick);
    }
}
