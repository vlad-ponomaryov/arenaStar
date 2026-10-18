using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// <see cref="EffectKind.Invisibility"/>: a timed status (hidden from enemies at any distance, D-024) that also ends the
    /// reveal window of an earlier attack at once — "Smoke" hides now, not 1 s later (D-061). Attacking again ends it
    /// (<c>AbilitySystem.Activate</c>).
    /// </summary>
    public static class InvisibilityEffect
    {
        public static void Apply(MatchState state, SimEventBuffer events, in EffectContext ctx, EffectDef effect, int target)
        {
            if (!StatusSystem.Apply(state, events, target, ctx.Source, ctx.SourceTeam, EffectKind.Invisibility, effect.Value, effect.DurationTicks))
                return;
            ref FighterState t = ref state.Fighters[target];
            int tick = state.Globals.Tick;
            if (t.RevealedUntilTick > tick) t.RevealedUntilTick = tick;
        }
    }
}
