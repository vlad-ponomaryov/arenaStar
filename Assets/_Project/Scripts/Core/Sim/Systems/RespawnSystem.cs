using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Respawn (GDD §5.2): a defeated fighter returns at its slot's spawn point with full health and charges once
    /// <see cref="FighterState.RespawnTick"/> is reached (<see cref="CombatRules.RespawnTicks"/> after the defeat).
    /// Reuses <see cref="SpawnSystem.Respawn"/> (keeps half the ult charge and the trick budget); statistics are kept (separate array). Modes without respawn — S2.7+.
    /// </summary>
    public static class RespawnSystem
    {
        public static void Run(MatchState state, MatchDefs defs, MatchSetup setup, SimEventBuffer events)
        {
            int count = state.Globals.FighterCount;
            int tick = state.Globals.Tick;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || f.Alive || tick < f.RespawnTick) continue;
                SpawnSystem.Respawn(state, defs, setup, i, events);
            }
        }
    }
}
