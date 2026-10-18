using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Applies an ability's effects to one target (D-011): each effect checks its <see cref="TargetFilter"/> against the
    /// relation source → target, then a small handler runs per <see cref="EffectKind"/>. Values are level-scaled by the
    /// source's level (GDD §6). S2.4 adds the other kinds here (one <c>case</c> + one static handler each).
    /// </summary>
    public static class EffectSystem
    {
        /// <param name="source">Fighter slot that owns the ability (may be defeated already), or −1.</param>
        /// <param name="sourceTeam">Team of the source at the time of the shot.</param>
        public static void ApplyAbility(MatchState state, MatchDefs defs, SimEventBuffer events, AbilityDef ability,
            int source, int sourceTeam, int target)
        {
            TargetFilter relation = Relation(source, sourceTeam, target, state.Fighters[target].Team);
            int level = source >= 0 ? state.Fighters[source].Level : 1;
            EffectDef[] effects = ability.Effects;
            for (int i = 0; i < effects.Length; i++)
            {
                EffectDef e = effects[i];
                if ((e.Filter & relation) == 0) continue;
                switch (e.Kind)
                {
                    case EffectKind.Damage:
                        DamageSystem.ApplyDamage(state, defs, events, source, target, defs.Rules.ScaleByLevel(e.Value, level));
                        break;
                }
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
