using System;
using Arena.Core.Sim;

namespace Arena.Core.State
{
    /// <summary>
    /// The complete mutable state of a match (D-002): scalar globals plus fixed-capacity arrays of blittable structs
    /// (capacities from <see cref="SimLimits"/>). Allocated once; a snapshot is another instance filled by
    /// <see cref="CopyFrom"/> (no allocations). Events of a tick are NOT part of the state (D-008).
    /// When adding an array: allocate it here and extend <see cref="CopyFrom"/>, <see cref="ContentEquals"/> and <see cref="Clear"/>.
    /// </summary>
    public sealed class MatchState
    {
        public MatchGlobals Globals;
        public readonly FighterState[] Fighters = new FighterState[SimLimits.MaxFighters];
        public readonly FighterStats[] Stats = new FighterStats[SimLimits.MaxFighters];
        public readonly ProjectileState[] Projectiles = new ProjectileState[SimLimits.MaxProjectiles];
        public readonly AreaState[] Areas = new AreaState[SimLimits.MaxAreas];
        public readonly StatusEffectState[] Statuses = new StatusEffectState[SimLimits.MaxStatusEffects];

        public int Tick => Globals.Tick;
        public int FighterCount => Globals.FighterCount;

        /// <summary>Copies everything from <paramref name="other"/> into this instance (snapshot / restore).</summary>
        public void CopyFrom(MatchState other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return;
            Globals = other.Globals;
            Array.Copy(other.Fighters, Fighters, Fighters.Length);
            Array.Copy(other.Stats, Stats, Stats.Length);
            Array.Copy(other.Projectiles, Projectiles, Projectiles.Length);
            Array.Copy(other.Areas, Areas, Areas.Length);
            Array.Copy(other.Statuses, Statuses, Statuses.Length);
        }

        /// <summary>Field-by-field equality of the whole state (determinism and snapshot checks).</summary>
        public bool ContentEquals(MatchState other)
        {
            if (other == null) return false;
            if (!Globals.Equals(other.Globals)) return false;
            for (int i = 0; i < Fighters.Length; i++)
                if (!Fighters[i].Equals(other.Fighters[i]) || !Stats[i].Equals(other.Stats[i])) return false;
            for (int i = 0; i < Projectiles.Length; i++)
                if (!Projectiles[i].Equals(other.Projectiles[i])) return false;
            for (int i = 0; i < Areas.Length; i++)
                if (!Areas[i].Equals(other.Areas[i])) return false;
            for (int i = 0; i < Statuses.Length; i++)
                if (!Statuses[i].Equals(other.Statuses[i])) return false;
            return true;
        }

        /// <summary>Clears all state to defaults (inactive slots, tick 0).</summary>
        public void Clear()
        {
            Globals = default;
            Array.Clear(Fighters, 0, Fighters.Length);
            Array.Clear(Stats, 0, Stats.Length);
            Array.Clear(Projectiles, 0, Projectiles.Length);
            Array.Clear(Areas, 0, Areas.Length);
            Array.Clear(Statuses, 0, Statuses.Length);
        }
    }
}
