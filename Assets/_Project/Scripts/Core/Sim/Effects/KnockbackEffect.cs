using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// <see cref="EffectKind.Knockback"/>: the target is pushed <see cref="EffectDef.Distance"/> cells over
    /// <see cref="EffectDef.DurationTicks"/> (at least 1) by <c>MovementSystem</c>, starting next tick, ignoring its own
    /// movement. Direction: the delivery direction, else away from the origin, else the source facing. A new push replaces
    /// the old one and cancels the target's dash.
    /// </summary>
    public static class KnockbackEffect
    {
        public static void Apply(MatchState state, in EffectContext ctx, EffectDef effect, int target)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive) return;

            Vec2 dir = ctx.Direction;
            if (dir.IsZero) dir = t.Position - ctx.Origin;
            if (dir.IsZero && ctx.Source >= 0) dir = state.Fighters[ctx.Source].Facing;
            if (dir.IsZero) return;

            int ticks = effect.DurationTicks > 0 ? effect.DurationTicks : 1;
            t.KnockbackVelocity = dir.Normalized * (effect.Distance / (ticks * SimTime.Dt));
            t.KnockbackTicks = ticks;
            DashSystem.Cancel(ref t);
        }
    }
}
