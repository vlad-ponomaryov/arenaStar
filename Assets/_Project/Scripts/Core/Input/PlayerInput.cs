using System;
using Arena.Core.Math;

namespace Arena.Core.Input
{
    /// <summary>
    /// Input of one fighter for one tick (D-006) — the analogue of Fusion's INetworkInput. Produced the same way by the
    /// local player (sticks, keyboard) and by bots. <see cref="Move"/> and <see cref="Aim"/> have length ≤ 1.
    /// <see cref="Aim"/> = direction × fraction of the attack range (lobbed attacks use the length); zero = no manual aim.
    /// <see cref="ViewDelayTicks"/> — how far behind the player saw the other fighters when making this input (lag compensation
    /// of instant contact hits, Server-Spec §2.8); offline and bots — 0 (no rewind).
    /// </summary>
    public struct PlayerInput : IEquatable<PlayerInput>
    {
        public Vec2 Move;
        public Vec2 Aim;
        public InputButtons Buttons;

        /// <summary>
        /// Rewind of the other fighters for this input's instant contact hits, ticks (<c>LagCompensation</c>): the targets are
        /// where the snapshot of tick <c>T + 1 − ViewDelayTicks</c> showed them. Untrusted — the server limits it (S3.13).
        /// </summary>
        public byte ViewDelayTicks;

        public PlayerInput(Vec2 move, Vec2 aim, InputButtons buttons)
        {
            Move = move;
            Aim = aim;
            Buttons = buttons;
            ViewDelayTicks = 0;
        }

        public PlayerInput(Vec2 move, Vec2 aim, InputButtons buttons, byte viewDelayTicks)
        {
            Move = move;
            Aim = aim;
            Buttons = buttons;
            ViewDelayTicks = viewDelayTicks;
        }

        public bool Has(InputButtons button) => (Buttons & button) != 0;

        /// <summary>
        /// Input made safe for the simulation (it is untrusted on a server): NaN/Infinity → zero, lengths clamped to 1,
        /// unknown button bits dropped; <see cref="ViewDelayTicks"/> is kept (the simulation bounds it, the server limits it).
        /// </summary>
        public PlayerInput Sanitized() => new PlayerInput(SanitizeAxis(Move), SanitizeAxis(Aim), Buttons & InputButtons.All, ViewDelayTicks);

        private static Vec2 SanitizeAxis(Vec2 v) => v.IsFinite ? Vec2.ClampLength(v, 1f) : Vec2.Zero;

        public bool Equals(PlayerInput other) =>
            Move == other.Move && Aim == other.Aim && Buttons == other.Buttons && ViewDelayTicks == other.ViewDelayTicks;
        public override bool Equals(object obj) => obj is PlayerInput other && Equals(other);
        public override int GetHashCode() => (Move.GetHashCode() * 397) ^ (Aim.GetHashCode() * 31) ^ (int)Buttons ^ (ViewDelayTicks << 8);
    }
}
