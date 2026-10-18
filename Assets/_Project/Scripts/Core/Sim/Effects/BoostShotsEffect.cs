using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// <see cref="EffectKind.BoostShots"/>: the next <see cref="EffectDef.Count"/> attack activations of the target deal
    /// +<see cref="EffectDef.Value"/> % damage (a burst is one activation; <c>AttackSystem</c> spends the counter at the press).
    /// A new boost replaces the old one; defeat clears it (<c>DamageSystem</c>).
    /// </summary>
    public static class BoostShotsEffect
    {
        public static void Apply(MatchState state, SimEventBuffer events, in EffectContext ctx, EffectDef effect, int target)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive) return;
            t.BoostedShotsLeft = effect.Count;
            t.BoostPercent = effect.Value;
            events.Add(new SimEvent(SimEventType.StatusApplied, state.Globals.Tick, ctx.Source, target, (int)EffectKind.BoostShots,
                t.Position, Vec2.Zero));
        }
    }
}
