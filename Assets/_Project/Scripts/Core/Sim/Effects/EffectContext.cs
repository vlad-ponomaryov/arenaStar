using Arena.Core.Defs;
using Arena.Core.Math;

namespace Arena.Core.Sim.Effects
{
    /// <summary>
    /// Who applies an ability's effects and from where (built by the delivery: projectile, cone, dash, area). Lives only
    /// for the duration of a call — not part of the state, so the <see cref="Ability"/> reference is fine (D-002).
    /// </summary>
    public readonly struct EffectContext
    {
        public readonly AbilityDef Ability;

        /// <summary><see cref="MatchDefs.MakeAbilityRef"/> of the ability, or −1 (tests, environment).</summary>
        public readonly int AbilityRef;

        /// <summary>Fighter slot that owns the ability (may be defeated already), or −1.</summary>
        public readonly int Source;

        /// <summary>Team of the source at the time of the shot.</summary>
        public readonly int SourceTeam;

        /// <summary>Point the effect comes from: hit point, caster position, area centre.</summary>
        public readonly Vec2 Origin;

        /// <summary>Unit direction of the delivery (projectile flight, dash), or zero = "away from <see cref="Origin"/>".</summary>
        public readonly Vec2 Direction;

        public EffectContext(AbilityDef ability, int abilityRef, int source, int sourceTeam, Vec2 origin, Vec2 direction)
        {
            Ability = ability;
            AbilityRef = abilityRef;
            Source = source;
            SourceTeam = sourceTeam;
            Origin = origin;
            Direction = direction;
        }

        /// <summary>True if damage of this ability charges the source's ult (only the main attack does, D-052).</summary>
        public bool ChargesUlt => AbilityRef >= 0 && MatchDefs.SlotOf(AbilityRef) == AbilitySlot.Attack;
    }
}
