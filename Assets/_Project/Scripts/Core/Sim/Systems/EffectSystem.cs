using Arena.Core.Defs;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Applies an ability's effects to one target (D-011): each effect checks its <see cref="TargetFilter"/> against the
    /// relation source → target, then a small static handler runs per <see cref="EffectKind"/> (<c>Arena.Core.Sim.Effects</c>).
    /// A new effect kind = one enum value + one handler + one <c>case</c> here; fighters only combine kinds in data.
    /// Effects run in the order of <see cref="AbilityDef.Effects"/>; a target defeated by an earlier effect ignores the rest.
    /// </summary>
    public static class EffectSystem
    {
        public static void ApplyAbility(MatchState state, MatchDefs defs, SimEventBuffer events, in EffectContext ctx, int target)
        {
            TargetFilter relation = Relation(ctx.Source, ctx.SourceTeam, target, state.Fighters[target].Team);
            EffectDef[] effects = ctx.Ability.Effects;
            for (int i = 0; i < effects.Length; i++)
            {
                EffectDef e = effects[i];
                if ((e.Filter & relation) == 0) continue;
                if (!state.Fighters[target].Alive) return;
                Apply(state, defs, events, in ctx, e, target);
            }
        }

        private static void Apply(MatchState state, MatchDefs defs, SimEventBuffer events, in EffectContext ctx, EffectDef e, int target)
        {
            switch (e.Kind)
            {
                case EffectKind.Damage:
                    DamageEffect.Apply(state, defs, events, in ctx, e, target);
                    break;
                case EffectKind.Heal:
                    HealEffect.Apply(state, defs, events, in ctx, e, target);
                    break;
                case EffectKind.Knockback:
                    KnockbackEffect.Apply(state, in ctx, e, target);
                    break;
                case EffectKind.Stun:
                    StunEffect.Apply(state, events, in ctx, e, target);
                    break;
                case EffectKind.Slow:
                case EffectKind.DamageReduction:
                case EffectKind.Mark:
                    StatusEffect.Apply(state, events, in ctx, e, target);
                    break;
                case EffectKind.Invisibility:
                    InvisibilityEffect.Apply(state, events, in ctx, e, target);
                    break;
                case EffectKind.TeamReveal:
                    TeamRevealEffect.Apply(state, events, in ctx, e, target);
                    break;
                case EffectKind.BoostShots:
                    BoostShotsEffect.Apply(state, events, in ctx, e, target);
                    break;
            }
        }

        /// <summary>Relation of a target to the source: <c>Self</c>, <c>Ally</c> (same team) or <c>Enemy</c>.</summary>
        public static TargetFilter Relation(int source, int sourceTeam, int target, int targetTeam)
        {
            if (target == source) return TargetFilter.Self;
            return targetTeam == sourceTeam ? TargetFilter.Ally : TargetFilter.Enemy;
        }

        /// <summary>True if the fighter in <paramref name="target"/> is alive and some effect of the ability may affect it.</summary>
        public static bool CanAffect(MatchState state, AbilityDef ability, int source, int sourceTeam, int target)
        {
            ref FighterState t = ref state.Fighters[target];
            if (!t.Active || !t.Alive) return false;
            return ability.Affects(Relation(source, sourceTeam, target, t.Team));
        }
    }
}
