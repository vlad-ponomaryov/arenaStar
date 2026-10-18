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
    /// Fields that survive a respawn (ult charge, trick uses/cooldown) are carried over by <c>SpawnSystem.Respawn</c>.
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

        /// <summary>Tick at which a defeated fighter respawns (valid while <see cref="Alive"/> is false).</summary>
        public int RespawnTick;

        /// <summary>Shots of the current attack burst still to fire (Pix: 3 after the first drop).</summary>
        public int BurstShotsLeft;

        /// <summary>Tick of the next burst shot.</summary>
        public int NextBurstTick;

        /// <summary>Direction fixed when the burst started (all shots of a burst fly the same way).</summary>
        public Vec2 BurstDir;

        /// <summary>Ult charge = damage dealt by the main attack, capped at the requirement (<c>UltSystem</c>).</summary>
        public int UltCharge;

        /// <summary>Trick uses left in this match.</summary>
        public int TrickUsesLeft;

        /// <summary>First tick the trick may be used again (cooldown).</summary>
        public int TrickReadyTick;

        /// <summary>Trick / talent available for this fighter (level or <c>MatchSetup.UnlockAllAbilities</c>), set at spawn.</summary>
        public bool TrickUnlocked;
        public bool TalentUnlocked;

        // --- Aggregates of the active statuses (StatusSystem recomputes them; neutral = 1 / false / 0). ---

        /// <summary>Movement speed multiplier from slows (1 = normal).</summary>
        public float SpeedMul;

        /// <summary>Damage taken multiplier from damage reduction (1 = normal, 0.6 = −40 %).</summary>
        public float DamageTakenMul;

        public bool Stunned;
        public bool Invisible;

        /// <summary>Bit per team that has marked this fighter (visible to that team, D-024).</summary>
        public uint MarkedByTeamMask;

        // --- Forced movement. ---

        /// <summary>Knockback velocity, cells per second, applied for <see cref="KnockbackTicks"/> more ticks.</summary>
        public Vec2 KnockbackVelocity;
        public int KnockbackTicks;

        /// <summary>Dash velocity, cells per second, applied for <see cref="DashTicksLeft"/> more ticks.</summary>
        public Vec2 DashVelocity;
        public int DashTicksLeft;

        /// <summary>Ability whose effects the dash applies to touched fighters (−1 when not dashing).</summary>
        public int DashAbilityRef;

        /// <summary>Bit per fighter slot already hit by the current dash (own bit set at start).</summary>
        public uint DashHitMask;

        /// <summary>Buttons of the current tick's input and of the previous tick (rising-edge detection, D-006).</summary>
        public InputButtons Buttons;
        public InputButtons PrevButtons;

        /// <summary>True on the tick the button goes from released to pressed.</summary>
        public bool WasPressed(InputButtons button) => (Buttons & button) != 0 && (PrevButtons & button) == 0;

        public bool IsHeld(InputButtons button) => (Buttons & button) != 0;

        public bool IsDashing => DashTicksLeft > 0;

        public bool Equals(FighterState o)
        {
            return Active == o.Active && Alive == o.Alive && DefIndex == o.DefIndex && Team == o.Team && Level == o.Level
                && Position == o.Position && Velocity == o.Velocity && Facing == o.Facing
                && Health == o.Health && MaxHealth == o.MaxHealth
                && Charges == o.Charges && ChargeProgressTicks == o.ChargeProgressTicks
                && LastCombatTick == o.LastCombatTick && RegenRemainder == o.RegenRemainder
                && RespawnTick == o.RespawnTick && BurstShotsLeft == o.BurstShotsLeft && NextBurstTick == o.NextBurstTick
                && BurstDir == o.BurstDir && UltCharge == o.UltCharge
                && TrickUsesLeft == o.TrickUsesLeft && TrickReadyTick == o.TrickReadyTick
                && TrickUnlocked == o.TrickUnlocked && TalentUnlocked == o.TalentUnlocked
                && SpeedMul == o.SpeedMul && DamageTakenMul == o.DamageTakenMul && Stunned == o.Stunned
                && Invisible == o.Invisible && MarkedByTeamMask == o.MarkedByTeamMask
                && KnockbackVelocity == o.KnockbackVelocity && KnockbackTicks == o.KnockbackTicks
                && DashVelocity == o.DashVelocity && DashTicksLeft == o.DashTicksLeft
                && DashAbilityRef == o.DashAbilityRef && DashHitMask == o.DashHitMask
                && Buttons == o.Buttons && PrevButtons == o.PrevButtons;
        }

        public override bool Equals(object obj) => obj is FighterState other && Equals(other);
        public override int GetHashCode() => (DefIndex * 397) ^ (Team * 31) ^ Health ^ Position.GetHashCode();
    }
}
