using System;

namespace Arena.Core.Input
{
    /// <summary>
    /// Buttons of <see cref="PlayerInput"/> (D-006). The local collector latches a press until the next tick; the simulation
    /// additionally reacts only to the rising edge (<c>FighterState.WasPressed</c>), so a repeated input is not a new press.
    /// <c>AutoAim</c> modifies <c>Attack</c>/<c>Ult</c>: "fire at the nearest visible enemy" (tap on the stick, D-007).
    /// </summary>
    [Flags]
    public enum InputButtons : byte
    {
        None = 0,
        Attack = 1 << 0,
        Ult = 1 << 1,
        Trick = 1 << 2,
        AutoAim = 1 << 3,

        All = Attack | Ult | Trick | AutoAim
    }
}
