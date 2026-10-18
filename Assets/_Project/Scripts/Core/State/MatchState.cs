using System;
using Arena.Core.Sim;

namespace Arena.Core.State
{
    /// <summary>
    /// The complete mutable state of a match (D-002): scalar globals plus fixed-capacity arrays of blittable structs
    /// (capacities from <see cref="SimLimits"/>). Allocated once; a snapshot is another instance filled by
    /// <see cref="CopyFrom"/> (no allocations). Events of a tick are NOT part of the state (D-008).
    /// When adding an array: allocate it here and extend <see cref="CopyFrom"/> and <see cref="ContentEquals"/>.
    /// </summary>
    public sealed class MatchState
    {
        public MatchGlobals Globals;
        public readonly FighterState[] Fighters = new FighterState[SimLimits.MaxFighters];

        public int Tick => Globals.Tick;
        public int FighterCount => Globals.FighterCount;

        /// <summary>Copies everything from <paramref name="other"/> into this instance (snapshot / restore).</summary>
        public void CopyFrom(MatchState other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return;
            Globals = other.Globals;
            Array.Copy(other.Fighters, Fighters, Fighters.Length);
        }

        /// <summary>Field-by-field equality of the whole state (determinism and snapshot checks).</summary>
        public bool ContentEquals(MatchState other)
        {
            if (other == null) return false;
            if (!Globals.Equals(other.Globals)) return false;
            for (int i = 0; i < Fighters.Length; i++)
                if (!Fighters[i].Equals(other.Fighters[i])) return false;
            return true;
        }

        /// <summary>Clears all state to defaults (inactive slots, tick 0).</summary>
        public void Clear()
        {
            Globals = default;
            Array.Clear(Fighters, 0, Fighters.Length);
        }
    }
}
