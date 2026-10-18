using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Passive talents (GDD §5.2, D-011): no state and no tick step — the condition is evaluated from the current state
    /// whenever a modifier is needed (movement speed in <c>MovementSystem</c>, damage dealt in <c>DamageEffect</c>).
    /// Open from <see cref="CombatRules.TalentUnlockLevel"/> or with <c>MatchSetup.UnlockAllAbilities</c> (D-012).
    /// </summary>
    public static class TalentSystem
    {
        /// <summary>True if the fighter in <paramref name="slot"/> has an unlocked talent whose condition holds now.</summary>
        public static bool IsActive(MatchState state, MatchDefs defs, int slot)
        {
            if (slot < 0) return false;
            ref FighterState f = ref state.Fighters[slot];
            if (!f.Active || !f.Alive || !f.TalentUnlocked) return false;
            TalentDef talent = defs.Fighters[f.DefIndex].Talent;
            if (talent == null) return false;
            switch (talent.Condition)
            {
                case TalentCondition.Always:
                    return true;
                case TalentCondition.UltCharged:
                    return UltSystem.IsCharged(defs, in f);
                case TalentCondition.HealthBelow:
                    return (long)f.Health * 100 < (long)f.MaxHealth * talent.Param;
                default:
                    return false;
            }
        }

        public static float MoveSpeedMultiplier(MatchState state, MatchDefs defs, int slot) =>
            Multiplier(state, defs, slot, TalentModifier.MoveSpeedMul);

        public static float DamageMultiplier(MatchState state, MatchDefs defs, int slot) =>
            Multiplier(state, defs, slot, TalentModifier.DamageMul);

        /// <summary><c>1 + Value %</c> if the fighter's active talent has this modifier, otherwise 1.</summary>
        public static float Multiplier(MatchState state, MatchDefs defs, int slot, TalentModifier modifier)
        {
            if (!IsActive(state, defs, slot)) return 1f;
            TalentDef talent = defs.Fighters[state.Fighters[slot].DefIndex].Talent;
            return talent.Modifier == modifier ? 1f + MathUtil.FromPercent(talent.Value) : 1f;
        }
    }
}
