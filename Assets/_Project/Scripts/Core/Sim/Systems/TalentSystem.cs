using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Passive talents (GDD §5.2, D-011): no state and no tick step — the condition is evaluated from the current state
    /// whenever a modifier is needed (movement speed in <c>MovementSystem</c>, damage dealt in <c>DamageEffect</c>, regen delay
    /// in <c>RegenSystem</c>, charges on a kill in <c>DamageSystem</c>).
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
                case TalentCondition.AllyAura:
                    return true; // the aura is on; who is inside is checked by its user (RegenDelayTicks)
                case TalentCondition.TargetMarked:
                    return true; // the target is checked by its user (DamageMultiplier)
                case TalentCondition.OnKill:
                    return true; // triggered by OnKill
                default:
                    return false;
            }
        }

        public static float MoveSpeedMultiplier(MatchState state, MatchDefs defs, int slot) =>
            Multiplier(state, defs, slot, TalentModifier.MoveSpeedMul);

        /// <summary>Damage multiplier of the fighter in <paramref name="slot"/> against <paramref name="target"/> (−1 = any
        /// target): a <see cref="TalentCondition.TargetMarked"/> talent only counts against targets marked by its team.</summary>
        public static float DamageMultiplier(MatchState state, MatchDefs defs, int slot, int target = -1)
        {
            float mul = Multiplier(state, defs, slot, TalentModifier.DamageMul);
            if (mul == 1f) return 1f;
            if (defs.Fighters[state.Fighters[slot].DefIndex].Talent.Condition != TalentCondition.TargetMarked) return mul;
            if (target < 0) return 1f;
            int team = state.Fighters[slot].Team;
            return (uint)team < 32u && (state.Fighters[target].MarkedByTeamMask & (1u << team)) != 0 ? mul : 1f;
        }

        /// <summary>
        /// The fighter in <paramref name="slot"/> defeated an enemy: an unlocked <see cref="TalentCondition.OnKill"/> +
        /// <see cref="TalentModifier.RefillCharges"/> talent refills all attack charges at once (Vex's "Agility").
        /// </summary>
        public static void OnKill(MatchState state, MatchDefs defs, SimEventBuffer events, int slot)
        {
            if (slot < 0 || !IsActive(state, defs, slot)) return;
            ref FighterState f = ref state.Fighters[slot];
            TalentDef talent = defs.Fighters[f.DefIndex].Talent;
            if (talent.Condition != TalentCondition.OnKill || talent.Modifier != TalentModifier.RefillCharges) return;
            if (f.Charges >= defs.Rules.MaxCharges) return;
            f.Charges = defs.Rules.MaxCharges;
            f.ChargeProgressTicks = 0;
            events.Add(new SimEvent(SimEventType.ChargeRestored, state.Globals.Tick, slot, -1, f.Charges, f.Position, Vec2.Zero));
        }

        /// <summary>Radius multiplier of the areas the fighter creates (Chomp's "Big Boom").</summary>
        public static float AreaRadiusMultiplier(MatchState state, MatchDefs defs, int slot) =>
            Multiplier(state, defs, slot, TalentModifier.AreaRadiusMul);

        /// <summary>
        /// Regeneration delay of the fighter in <paramref name="slot"/>: the rules' delay, or the shortest
        /// <see cref="TalentDef.Ticks"/> of an alive ally (not itself) with an active <see cref="TalentModifier.RegenDelay"/>
        /// aura within <see cref="TalentDef.Param"/> cells (Kora's "Herbal Blend": 2 s instead of 3).
        /// </summary>
        public static int RegenDelayTicks(MatchState state, MatchDefs defs, int slot)
        {
            int delay = defs.Rules.RegenDelayTicks;
            ref FighterState f = ref state.Fighters[slot];
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (j == slot) continue;
                ref FighterState o = ref state.Fighters[j];
                if (o.Team != f.Team || !IsActive(state, defs, j)) continue;
                TalentDef talent = defs.Fighters[o.DefIndex].Talent;
                if (talent.Modifier != TalentModifier.RegenDelay || talent.Condition != TalentCondition.AllyAura) continue;
                if (talent.Ticks >= delay) continue;
                float r = talent.Param;
                if (Vec2.SqrDistance(o.Position, f.Position) <= r * r) delay = talent.Ticks;
            }
            return delay;
        }

        /// <summary><c>1 + Value %</c> if the fighter's active talent has this modifier, otherwise 1.</summary>
        public static float Multiplier(MatchState state, MatchDefs defs, int slot, TalentModifier modifier)
        {
            if (!IsActive(state, defs, slot)) return 1f;
            TalentDef talent = defs.Fighters[state.Fighters[slot].DefIndex].Talent;
            return talent.Modifier == modifier ? 1f + MathUtil.FromPercent(talent.Value) : 1f;
        }
    }
}
