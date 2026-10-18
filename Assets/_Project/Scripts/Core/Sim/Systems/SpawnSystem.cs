using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Puts a fighter into its slot at the spawn point: full (level-scaled) health and charges, facing the arena centre.
    /// Used at match start; the respawn system (S2.3) reuses it.
    /// </summary>
    public static class SpawnSystem
    {
        public static void SpawnFighter(MatchState state, MatchDefs defs, in PlayerSlotSetup slot, int slotIndex, SimEventBuffer events)
        {
            FighterDef def = defs.Fighters[slot.FighterIndex];
            ref FighterState f = ref state.Fighters[slotIndex];
            int tick = state.Globals.Tick;

            f = default;
            f.Active = true;
            f.Alive = true;
            f.DefIndex = slot.FighterIndex;
            f.Team = slot.Team;
            f.Level = slot.Level;
            f.Position = defs.Bounds.ClampCircle(slot.SpawnPosition, def.Radius);
            f.Velocity = Vec2.Zero;

            Vec2 toCentre = (defs.Bounds.Center - f.Position).Normalized;
            f.Facing = toCentre.IsZero ? Vec2.Up : toCentre;

            f.MaxHealth = defs.Rules.ScaleByLevel(def.MaxHealth, slot.Level);
            f.Health = f.MaxHealth;
            f.Charges = defs.Rules.MaxCharges;
            f.ChargeProgressTicks = 0;
            f.LastCombatTick = tick;
            f.RegenRemainder = 0f;

            events.Add(new SimEvent(SimEventType.FighterSpawned, tick, slotIndex, -1, 0, f.Position, f.Facing));
        }
    }
}
