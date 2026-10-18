using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// All static data a match simulation needs (built by Data's ConfigConverter or by tests). Read-only during the match;
    /// state refers to entries by index (<c>FighterState.DefIndex</c>), never by reference.
    /// </summary>
    public sealed class MatchDefs
    {
        public CombatRules Rules;
        public FighterDef[] Fighters;

        /// <summary>Temporary arena rectangle until the grid map (S2.5).</summary>
        public ArenaBounds Bounds;

        /// <summary>Index of the fighter with <paramref name="id"/> in <see cref="Fighters"/>, or −1.</summary>
        public int IndexOfFighter(string id)
        {
            if (Fighters == null) return -1;
            for (int i = 0; i < Fighters.Length; i++)
                if (Fighters[i] != null && string.Equals(Fighters[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        public void Validate()
        {
            if (Rules == null) throw new ArgumentException("MatchDefs.Rules is null");
            Rules.Validate();
            if (Fighters == null || Fighters.Length == 0) throw new ArgumentException("MatchDefs.Fighters is empty");
            for (int i = 0; i < Fighters.Length; i++)
            {
                if (Fighters[i] == null) throw new ArgumentException("MatchDefs.Fighters[" + i + "] is null");
                Fighters[i].Validate();
                if (IndexOfFighter(Fighters[i].Id) != i) throw new ArgumentException("MatchDefs: duplicate fighter id " + Fighters[i].Id);
            }
            if (!Bounds.IsValid) throw new ArgumentException("MatchDefs.Bounds is empty");
        }
    }
}
