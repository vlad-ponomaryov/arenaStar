using System;
using Arena.Core.Math;

namespace Arena.Core.Input
{
    /// <summary>
    /// Input of one fighter for one tick (D-006) — the analogue of Fusion's INetworkInput. Produced the same way by the
    /// local player (sticks, keyboard) and by bots. <see cref="Move"/> and <see cref="Aim"/> have length ≤ 1.
    /// <see cref="Aim"/> = direction × fraction of the attack range (lobbed attacks use the length); zero = no manual aim.
    /// </summary>
    public struct PlayerInput : IEquatable<PlayerInput>
    {
        public Vec2 Move;
        public Vec2 Aim;
        public InputButtons Buttons;

        public PlayerInput(Vec2 move, Vec2 aim, InputButtons buttons)
        {
            Move = move;
            Aim = aim;
            Buttons = buttons;
        }

        public bool Has(InputButtons button) => (Buttons & button) != 0;

        /// <summary>
        /// Input made safe for the simulation (it is untrusted on a server): NaN/Infinity → zero, lengths clamped to 1,
        /// unknown button bits dropped.
        /// </summary>
        public PlayerInput Sanitized() => new PlayerInput(SanitizeAxis(Move), SanitizeAxis(Aim), Buttons & InputButtons.All);

        private static Vec2 SanitizeAxis(Vec2 v) => v.IsFinite ? Vec2.ClampLength(v, 1f) : Vec2.Zero;

        public bool Equals(PlayerInput other) => Move == other.Move && Aim == other.Aim && Buttons == other.Buttons;
        public override bool Equals(object obj) => obj is PlayerInput other && Equals(other);
        public override int GetHashCode() => (Move.GetHashCode() * 397) ^ (Aim.GetHashCode() * 31) ^ (int)Buttons;
    }
}
