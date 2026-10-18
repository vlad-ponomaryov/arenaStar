using System;
using Arena.Core.Sim;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Per-match parameters chosen at launch (slots, seed). Unlike <see cref="MatchDefs"/> it changes from match to match.
    /// Slot index = fighter index in the state = index in the <c>PlayerInput[]</c> passed to <c>MatchSimulation.Tick</c>.
    /// </summary>
    public sealed class MatchSetup
    {
        /// <summary>Seed of the match RNG (deterministic replays and tests).</summary>
        public int Seed;

        public PlayerSlotSetup[] Slots;

        /// <summary>
        /// Trick and talent are available regardless of level (D-012: true in the prototype). When false they open at
        /// <see cref="CombatRules.TrickUnlockLevel"/> / <see cref="CombatRules.TalentUnlockLevel"/>.
        /// </summary>
        public bool UnlockAllAbilities = true;

        public int SlotCount => Slots == null ? 0 : Slots.Length;

        public void Validate(MatchDefs defs)
        {
            if (Slots == null || Slots.Length == 0) throw new ArgumentException("MatchSetup.Slots is empty");
            if (Slots.Length > SimLimits.MaxFighters)
                throw new ArgumentException("MatchSetup: " + Slots.Length + " slots > SimLimits.MaxFighters " + SimLimits.MaxFighters);
            for (int i = 0; i < Slots.Length; i++)
            {
                PlayerSlotSetup s = Slots[i];
                if (s.FighterIndex < 0 || s.FighterIndex >= defs.Fighters.Length)
                    throw new ArgumentException("MatchSetup.Slots[" + i + "]: fighter index " + s.FighterIndex + " out of range");
                if (s.Team < 0 || s.Team >= SimLimits.MaxTeams)
                    throw new ArgumentException("MatchSetup.Slots[" + i + "]: team " + s.Team + " out of range");
                if (s.Level < 1) throw new ArgumentException("MatchSetup.Slots[" + i + "]: level must be ≥ 1");
                if (!s.SpawnPosition.IsFinite) throw new ArgumentException("MatchSetup.Slots[" + i + "]: spawn is not finite");
            }
        }
    }
}
