using System;
using Arena.Core.Map;

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

        /// <summary>The map of the match (initial tiles, spawns); the mutable tiles are in <c>MatchState.Tiles</c>.</summary>
        public MapDef Map;

        /// <summary>Ability slots per fighter in an ability reference (<see cref="AbilitySlot"/>).</summary>
        public const int AbilitySlotsPerFighter = 3;

        /// <summary>
        /// Ability reference stored in state (projectiles, areas): an <c>int</c> instead of an object reference (D-002).
        /// </summary>
        public static int MakeAbilityRef(int fighterIndex, AbilitySlot slot) => fighterIndex * AbilitySlotsPerFighter + (int)slot;

        /// <summary>Slot part of an ability reference (−1 or another negative reference gives <see cref="AbilitySlot.Attack"/>
        /// — check the reference first).</summary>
        public static AbilitySlot SlotOf(int abilityRef) => abilityRef < 0 ? AbilitySlot.Attack : (AbilitySlot)(abilityRef % AbilitySlotsPerFighter);

        /// <summary>Ability by reference from <see cref="MakeAbilityRef"/>, or null.</summary>
        public AbilityDef GetAbility(int abilityRef)
        {
            if (abilityRef < 0) return null;
            int fighter = abilityRef / AbilitySlotsPerFighter;
            if (fighter >= Fighters.Length) return null;
            return Fighters[fighter].GetAbility((AbilitySlot)(abilityRef % AbilitySlotsPerFighter));
        }

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
            if (Map == null) throw new ArgumentException("MatchDefs.Map is null");
            Map.Validate();
        }
    }
}
