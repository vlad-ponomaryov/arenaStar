using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// Timed statuses without extra side effects — <see cref="EffectKind.Slow"/>, <see cref="EffectKind.DamageReduction"/>,
    /// <see cref="EffectKind.Invisibility"/>, <see cref="EffectKind.Mark"/>: one entry in <c>MatchState.Statuses</c>;
    /// <see cref="StatusSystem"/> folds it into the fighter aggregates. A new plain status kind needs only a line in
    /// <c>StatusSystem.Accumulate</c> and a <c>case</c> in <c>EffectSystem</c>.
    /// </summary>
    public static class StatusEffect
    {
        public static void Apply(MatchState state, SimEventBuffer events, in EffectContext ctx, EffectDef effect, int target)
        {
            StatusSystem.Apply(state, events, target, ctx.Source, ctx.SourceTeam, effect.Kind, effect.Value, effect.DurationTicks);
        }
    }
}
