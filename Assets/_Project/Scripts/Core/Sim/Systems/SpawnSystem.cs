using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Puts a fighter into its slot at the spawn point: full (level-scaled) health and charges, facing the arena centre,
    /// neutral statuses, trick uses and unlocks from the rules (D-012). <see cref="Respawn"/> additionally carries over what
    /// lasts the whole match: the ult charge (already reduced on defeat) and the trick uses / cooldown.
    /// </summary>
    public static class SpawnSystem
    {
        public static void SpawnFighter(MatchState state, MatchDefs defs, MatchSetup setup, int slotIndex, SimEventBuffer events)
        {
            PlayerSlotSetup slot = setup.Slots[slotIndex];
            FighterDef def = defs.Fighters[slot.FighterIndex];
            CombatRules rules = defs.Rules;
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

            f.MaxHealth = rules.ScaleByLevel(def.MaxHealth, slot.Level);
            f.Health = f.MaxHealth;
            f.Charges = rules.MaxCharges;
            f.ChargeProgressTicks = 0;
            f.LastCombatTick = tick;
            f.RegenRemainder = 0f;

            f.UltCharge = 0;
            f.TrickUsesLeft = rules.TrickUses;
            f.TrickReadyTick = tick;
            f.TrickUnlocked = setup.UnlockAllAbilities || slot.Level >= rules.TrickUnlockLevel;
            f.TalentUnlocked = setup.UnlockAllAbilities || slot.Level >= rules.TalentUnlockLevel;
            StatusSystem.ResetAggregates(ref f);
            DashSystem.Cancel(ref f);

            events.Add(new SimEvent(SimEventType.FighterSpawned, tick, slotIndex, -1, 0, f.Position, f.Facing));
        }

        /// <summary>Respawn after a defeat: a fresh spawn that keeps the match-long ult charge and trick budget.</summary>
        public static void Respawn(MatchState state, MatchDefs defs, MatchSetup setup, int slotIndex, SimEventBuffer events)
        {
            ref FighterState f = ref state.Fighters[slotIndex];
            int ultCharge = f.UltCharge;
            int trickUses = f.TrickUsesLeft;
            int trickReady = f.TrickReadyTick;
            SpawnFighter(state, defs, setup, slotIndex, events);
            f.UltCharge = ultCharge;
            f.TrickUsesLeft = trickUses;
            f.TrickReadyTick = trickReady;
        }
    }
}
