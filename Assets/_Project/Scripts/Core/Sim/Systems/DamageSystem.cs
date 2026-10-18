using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// The single point where health changes in combat: damage (no friendly fire, GDD §5.2; × the target's
    /// <see cref="FighterState.DamageTakenMul"/>), healing, defeat, statistics and events. Effects call it; nothing else
    /// writes <see cref="FighterState.Health"/> during a tick (except regen). Damage of the main attack charges the
    /// attacker's ult (<see cref="UltSystem.AddCharge"/>). Defeat costs part of the ult charge and clears statuses,
    /// dash, knockback and the shot boost; the winner's on-kill talent fires (<see cref="TalentSystem.OnKill"/>).
    /// </summary>
    public static class DamageSystem
    {
        /// <summary>
        /// Removes up to <paramref name="amount"/> × damage-taken multiplier health from <paramref name="target"/>. Returns
        /// the health actually removed (0 if the target is not alive, the amount is not positive, or the source is an ally).
        /// </summary>
        /// <param name="source">Attacking fighter slot, or −1 for the environment.</param>
        /// <param name="chargesUlt">The damage comes from the source's main attack: it charges the source's ult.</param>
        /// <param name="abilityRef">Ability that deals the damage (carried by the <see cref="SimEventType.Damaged"/> event), or −1.</param>
        public static int ApplyDamage(MatchState state, MatchDefs defs, SimEventBuffer events, int source, int target, int amount,
            bool chargesUlt = false, int abilityRef = -1)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive || amount <= 0 || state.Globals.Predicting) return 0; // no damage in prediction (§2.5)
            if (source >= 0 && source != target && state.Fighters[source].Team == t.Team) return 0;
            if (t.DamageTakenMul != 1f) amount = MathUtil.RoundToInt(amount * t.DamageTakenMul);
            if (amount <= 0) return 0;

            int tick = state.Globals.Tick;
            int dealt = amount < t.Health ? amount : t.Health;
            t.Health -= dealt;
            RegenSystem.MarkInCombat(ref t, tick);
            if (source >= 0 && source != target) state.Stats[source].DamageDealt += dealt;
            events.Add(new SimEvent(SimEventType.Damaged, tick, source, target, dealt, t.Position, Vec2.Zero, abilityRef));
            if (chargesUlt && source != target) UltSystem.AddCharge(state, defs, events, source, dealt);

            if (t.Health <= 0) Defeat(state, defs, events, source, target);
            return dealt;
        }

        /// <summary>Restores up to <paramref name="amount"/> health (not above max). Returns the health actually restored.</summary>
        public static int ApplyHeal(MatchState state, SimEventBuffer events, int source, int target, int amount)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive || amount <= 0 || state.Globals.Predicting) return 0; // no healing in prediction (§2.5)

            int missing = t.MaxHealth - t.Health;
            int healed = amount < missing ? amount : missing;
            if (healed <= 0) return 0;
            t.Health += healed;
            if (source >= 0) state.Stats[source].HealingDone += healed;
            events.Add(new SimEvent(SimEventType.Healed, state.Globals.Tick, source, target, healed, t.Position, Vec2.Zero));
            return healed;
        }

        private static void Defeat(MatchState state, MatchDefs defs, SimEventBuffer events, int source, int target)
        {
            ref FighterState t = ref state.Fighters[target];
            int tick = state.Globals.Tick;
            t.Alive = false;
            t.Health = 0;
            t.Velocity = Vec2.Zero;
            t.BurstShotsLeft = 0;
            t.RespawnTick = tick + defs.Rules.RespawnTicks;
            t.KnockbackTicks = 0;
            t.KnockbackVelocity = Vec2.Zero;
            DashSystem.Cancel(ref t);
            StatusSystem.ClearTarget(state, target);
            UltSystem.OnDefeat(ref t, defs.Rules);
            AcornsRules.OnDefeat(state, defs, events, target);
            LastBeastRules.OnDefeat(state, defs, events, target);

            t.BoostedShotsLeft = 0;
            t.BoostPercent = 0;
            t.ShotBonusPercent = 0;

            state.Stats[target].Deaths++;
            if (source >= 0 && source != target) state.Stats[source].Kills++;
            events.Add(new SimEvent(SimEventType.FighterDefeated, tick, source, target, t.RespawnTick, t.Position, Vec2.Zero));
            if (source >= 0 && source != target) TalentSystem.OnKill(state, defs, events, source);
        }
    }
}
