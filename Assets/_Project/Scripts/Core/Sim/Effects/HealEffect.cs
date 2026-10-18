using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary><see cref="EffectKind.Heal"/>: value scaled by the source level, applied through <see cref="DamageSystem.ApplyHeal"/>.</summary>
    public static class HealEffect
    {
        public static void Apply(MatchState state, MatchDefs defs, SimEventBuffer events, in EffectContext ctx, EffectDef effect, int target)
        {
            int level = ctx.Source >= 0 ? state.Fighters[ctx.Source].Level : 1;
            DamageSystem.ApplyHeal(state, events, ctx.Source, target, defs.Rules.ScaleByLevel(effect.Value, level));
        }
    }
}
