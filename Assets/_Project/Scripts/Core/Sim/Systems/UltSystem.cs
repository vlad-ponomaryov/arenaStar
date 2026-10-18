using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Ult (GDD §5.2): the charge grows from damage dealt by the main attack (<see cref="AddCharge"/>, called by
    /// <see cref="DamageSystem"/>) up to <see cref="FighterDef.UltChargeRequired"/> (level-scaled like damage);
    /// on defeat <see cref="CombatRules.UltLossOnDeath"/> of it is lost. Used on the rising edge of
    /// <see cref="InputButtons.Ult"/> when fully charged; the charge drops to 0 and the ult is delivered by
    /// <see cref="AbilitySystem.Activate"/> with the same aim rules as the attack.
    /// </summary>
    public static class UltSystem
    {
        /// <summary>Charge needed by this fighter at its level (0 if it has no ult).</summary>
        public static int Required(MatchDefs defs, in FighterState f)
        {
            FighterDef def = defs.Fighters[f.DefIndex];
            return def.Ult == null ? 0 : defs.Rules.ScaleByLevel(def.UltChargeRequired, f.Level);
        }

        public static bool IsCharged(MatchDefs defs, in FighterState f)
        {
            int required = Required(defs, in f);
            return required > 0 && f.UltCharge >= required;
        }

        /// <summary>Charge 0..1 for the HUD (0 without an ult).</summary>
        public static float ChargeFraction(MatchDefs defs, in FighterState f)
        {
            int required = Required(defs, in f);
            return required > 0 ? MathUtil.Clamp01((float)f.UltCharge / required) : 0f;
        }

        /// <summary>Adds damage dealt to the ult charge of <paramref name="slot"/> (alive fighters with an ult only).</summary>
        public static void AddCharge(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, int amount)
        {
            if (slot < 0 || amount <= 0) return;
            ref FighterState f = ref state.Fighters[slot];
            if (!f.Active || !f.Alive) return;
            int required = Required(defs, in f);
            if (required <= 0 || f.UltCharge >= required) return;

            f.UltCharge += amount;
            if (f.UltCharge < required) return;
            f.UltCharge = required;
            events.Add(new SimEvent(SimEventType.UltReady, state.Globals.Tick, slot, -1, f.UltCharge, f.Position, Vec2.Zero));
        }

        /// <summary>Defeat: loses <see cref="CombatRules.UltLossOnDeath"/> of the charge (rounded half away from zero).</summary>
        public static void OnDefeat(ref FighterState f, CombatRules rules)
        {
            f.UltCharge -= MathUtil.RoundToInt(f.UltCharge * rules.UltLossOnDeath);
            if (f.UltCharge < 0) f.UltCharge = 0;
        }

        /// <summary>Uses the ult on the rising edge of the button if it is charged and the fighter can act.</summary>
        public static void TryUse(MatchState state, MatchDefs defs, in PlayerInput input, SimEventBuffer events, int slot)
        {
            ref FighterState f = ref state.Fighters[slot];
            AbilityDef ult = defs.Fighters[f.DefIndex].Ult;
            if (ult == null || !f.WasPressed(InputButtons.Ult)) return;
            if (!AbilitySystem.CanActivate(in f) || !IsCharged(defs, in f)) return;

            f.UltCharge = 0;
            Vec2 dir = AbilitySystem.ResolveDirection(state, slot, in input, ult);
            f.Facing = dir;
            AbilitySystem.Activate(state, defs, events, slot, ult, MatchDefs.MakeAbilityRef(f.DefIndex, AbilitySlot.Ult), dir,
                AbilitySystem.ResolveDistance(state, slot, in input, ult), EffectContext.FullDamagePercent, input.ViewDelayTicks);
        }
    }
}
