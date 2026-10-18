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

        /// <summary>Pulse of a field area (0-based) — damage/heal values are split over <see cref="PulseCount"/> pulses.</summary>
        public readonly int PulseIndex;

        /// <summary>Number of pulses the amount is split over (1 = the whole amount at once).</summary>
        public readonly int PulseCount;

        public EffectContext(AbilityDef ability, int abilityRef, int source, int sourceTeam, Vec2 origin, Vec2 direction)
            : this(ability, abilityRef, source, sourceTeam, origin, direction, 0, 1)
        {
        }

        public EffectContext(AbilityDef ability, int abilityRef, int source, int sourceTeam, Vec2 origin, Vec2 direction,
            int pulseIndex, int pulseCount)
        {
            Ability = ability;
            AbilityRef = abilityRef;
            Source = source;
            SourceTeam = sourceTeam;
            Origin = origin;
            Direction = direction;
            PulseIndex = pulseIndex;
            PulseCount = pulseCount < 1 ? 1 : pulseCount;
        }

        /// <summary>
        /// This pulse's share of a total <paramref name="amount"/>: <c>⌊amount·(k+1)/n⌋ − ⌊amount·k/n⌋</c>, so a target inside
        /// for all n pulses gets exactly the total (Kora's dome: 1000 over 3 s).
        /// </summary>
        public int Portion(int amount)
        {
            if (PulseCount <= 1) return amount;
            long next = (long)amount * (PulseIndex + 1) / PulseCount;
            long prev = (long)amount * PulseIndex / PulseCount;
            return (int)(next - prev);
        }

        /// <summary>True if damage of this ability charges the source's ult (only the main attack does, D-052).</summary>
        public bool ChargesUlt => AbilityRef >= 0 && MatchDefs.SlotOf(AbilityRef) == AbilitySlot.Attack;
    }
}
