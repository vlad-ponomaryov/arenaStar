using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// <see cref="EffectKind.Stun"/>: a timed status (no movement, no abilities — <c>MovementSystem</c>, <c>AbilitySystem</c>);
    /// also cancels the target's attack burst and dash at once. A knockback still moves a stunned fighter.
    /// </summary>
    public static class StunEffect
    {
        public static void Apply(MatchState state, SimEventBuffer events, in EffectContext ctx, EffectDef effect, int target)
        {
            if (!StatusSystem.Apply(state, events, target, ctx.Source, ctx.SourceTeam, EffectKind.Stun, effect.Value, effect.DurationTicks))
                return;
            ref FighterState t = ref state.Fighters[target];
            t.BurstShotsLeft = 0;
            DashSystem.Cancel(ref t);
        }
    }
}
