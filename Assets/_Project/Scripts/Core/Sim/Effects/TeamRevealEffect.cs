using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// <see cref="EffectKind.TeamReveal"/>: the source team sees every enemy on the map (bushes and invisibility included) for
    /// <see cref="EffectDef.DurationTicks"/> — a per-team timer in <see cref="MatchState.TeamRevealUntilTick"/> read by
    /// <c>VisibilitySystem</c> (D-024). Same timing as statuses (applied in tick T: the rest of T and T+1 … T+duration);
    /// the later end wins; survives the source's defeat (the team keeps the information).
    /// </summary>
    public static class TeamRevealEffect
    {
        public static void Apply(MatchState state, SimEventBuffer events, in EffectContext ctx, EffectDef effect, int target)
        {
            int team = ctx.SourceTeam;
            if (team < 0 || team >= SimLimits.MaxTeams || effect.DurationTicks <= 0) return;
            int tick = state.Globals.Tick;
            int until = tick + 1 + effect.DurationTicks;
            if (until > state.TeamRevealUntilTick[team]) state.TeamRevealUntilTick[team] = until;
            events.Add(new SimEvent(SimEventType.StatusApplied, tick, ctx.Source, target, (int)EffectKind.TeamReveal,
                state.Fighters[target].Position, Vec2.Zero));
        }
    }
}
