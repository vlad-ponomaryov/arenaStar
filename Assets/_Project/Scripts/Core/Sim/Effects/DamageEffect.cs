using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// <see cref="EffectKind.Damage"/>: value scaled by the source level (GDD §6), the source talent (also against this target:
    /// marked targets), the source's berries (Last Beast, additive) and the context's damage percent (shot boost, pierce falloff); the target's
    /// damage reduction is applied inside <see cref="DamageSystem"/> (the single point of health changes).
    /// </summary>
    public static class DamageEffect
    {
        public static void Apply(MatchState state, MatchDefs defs, SimEventBuffer events, in EffectContext ctx, EffectDef effect, int target)
        {
            int level = ctx.Source >= 0 ? state.Fighters[ctx.Source].Level : 1;
            int amount = defs.Rules.ScaleByLevel(effect.Value, level);
            float mul = TalentSystem.DamageMultiplier(state, defs, ctx.Source, target);
            if (mul != 1f) amount = MathUtil.RoundToInt(amount * mul);
            float berries = LastBeastRules.DamageMultiplier(state, defs, ctx.Source);
            if (berries != 1f) amount = MathUtil.RoundToInt(amount * berries);
            amount = ctx.Portion(ctx.ScaleDamage(amount));
            DamageSystem.ApplyDamage(state, defs, events, ctx.Source, target, amount, ctx.ChargesUlt, ctx.AbilityRef);
        }
    }
}
