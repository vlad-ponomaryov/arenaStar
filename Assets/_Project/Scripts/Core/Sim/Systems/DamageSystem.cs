using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// The single point where health changes in combat: damage (no friendly fire, GDD §5.2), healing, defeat, statistics
    /// and events. Effects call it; nothing else writes <see cref="FighterState.Health"/> during a tick (except regen).
    /// Ult charge from damage dealt: S2.4 adds it here (<see cref="FighterState.UltCharge"/>).
    /// </summary>
    public static class DamageSystem
    {
        /// <summary>
        /// Removes up to <paramref name="amount"/> health from <paramref name="target"/>. Returns the health actually removed
        /// (0 if the target is not alive, the amount is not positive, or the source is an ally of the target).
        /// </summary>
        /// <param name="source">Attacking fighter slot, or −1 for the environment.</param>
        public static int ApplyDamage(MatchState state, MatchDefs defs, SimEventBuffer events, int source, int target, int amount)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive || amount <= 0) return 0;
            if (source >= 0 && source != target && state.Fighters[source].Team == t.Team) return 0;

            int tick = state.Globals.Tick;
            int dealt = amount < t.Health ? amount : t.Health;
            t.Health -= dealt;
            RegenSystem.MarkInCombat(ref t, tick);
            if (source >= 0 && source != target) state.Stats[source].DamageDealt += dealt;
            events.Add(new SimEvent(SimEventType.Damaged, tick, source, target, dealt, t.Position, Vec2.Zero));

            if (t.Health <= 0) Defeat(state, defs, events, source, target);
            return dealt;
        }

        /// <summary>Restores up to <paramref name="amount"/> health (not above max). Returns the health actually restored.</summary>
        public static int ApplyHeal(MatchState state, SimEventBuffer events, int source, int target, int amount)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive || amount <= 0) return 0;

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

            state.Stats[target].Deaths++;
            if (source >= 0 && source != target) state.Stats[source].Kills++;
            events.Add(new SimEvent(SimEventType.FighterDefeated, tick, source, target, t.RespawnTick, t.Position, Vec2.Zero));
        }
    }
}
