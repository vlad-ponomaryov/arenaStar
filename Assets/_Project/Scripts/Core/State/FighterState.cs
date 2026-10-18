using System;
using Arena.Core.Input;
using Arena.Core.Math;

namespace Arena.Core.State
{
    /// <summary>
    /// Mutable state of one fighter slot (D-002): a blittable struct inside <see cref="MatchState.Fighters"/>, no references.
    /// Time is in ticks, health in <c>int</c> (D-003). <c>bool</c> fields become Fusion <c>NetworkBool</c> on stage 3
    /// (implicit conversions keep the code unchanged, D-046).
    /// When adding a field: also add it to <see cref="Equals(FighterState)"/> — StateEqualityTests checks every field.
    /// </summary>
    public struct FighterState : IEquatable<FighterState>
    {
        /// <summary>The slot is used in this match (slots ≥ setup slot count stay inactive).</summary>
        public bool Active;
        public bool Alive;

        /// <summary>Index in <c>MatchDefs.Fighters</c>.</summary>
        public int DefIndex;
        public int Team;
        public int Level;

        public Vec2 Position;

        /// <summary>Velocity applied during the last tick (cells/s), for view extrapolation and bots.</summary>
        public Vec2 Velocity;

        /// <summary>Unit look direction: aim if aiming, otherwise movement direction, otherwise unchanged.</summary>
        public Vec2 Facing;

        public int Health;

        /// <summary>Max health with the level bonus applied.</summary>
        public int MaxHealth;

        public int Charges;

        /// <summary>Ticks accumulated towards the next charge (0 when charges are full).</summary>
        public int ChargeProgressTicks;

        /// <summary>Last tick the fighter attacked or took damage (regeneration delay counts from it).</summary>
        public int LastCombatTick;

        /// <summary>Fractional health regenerated but not yet applied (0..1).</summary>
        public float RegenRemainder;

        /// <summary>Buttons of the current tick's input and of the previous tick (rising-edge detection, D-006).</summary>
        public InputButtons Buttons;
        public InputButtons PrevButtons;

        /// <summary>True on the tick the button goes from released to pressed.</summary>
        public bool WasPressed(InputButtons button) => (Buttons & button) != 0 && (PrevButtons & button) == 0;

        public bool IsHeld(InputButtons button) => (Buttons & button) != 0;

        public bool Equals(FighterState o)
        {
            return Active == o.Active && Alive == o.Alive && DefIndex == o.DefIndex && Team == o.Team && Level == o.Level
                && Position == o.Position && Velocity == o.Velocity && Facing == o.Facing
                && Health == o.Health && MaxHealth == o.MaxHealth
                && Charges == o.Charges && ChargeProgressTicks == o.ChargeProgressTicks
                && LastCombatTick == o.LastCombatTick && RegenRemainder == o.RegenRemainder
                && Buttons == o.Buttons && PrevButtons == o.PrevButtons;
        }

        public override bool Equals(object obj) => obj is FighterState other && Equals(other);
        public override int GetHashCode() => (DefIndex * 397) ^ (Team * 31) ^ Health ^ Position.GetHashCode();
    }
}
