using System;
using Arena.Core.Defs;

namespace Arena.Core.State
{
    /// <summary>
    /// A timed effect on a fighter (stun, slow, damage reduction, invisibility, mark) in <see cref="MatchState.Statuses"/>
    /// (D-002). Active during the ticks before <see cref="ExpireTick"/>; <c>StatusSystem</c> folds the active entries into
    /// the aggregates of <see cref="FighterState"/> (<c>SpeedMul</c>, <c>DamageTakenMul</c>, <c>Stunned</c>, …).
    /// The same source re-applying the same kind to the same target refreshes its entry instead of adding one.
    /// When adding a field: also add it to <see cref="Equals(StatusEffectState)"/> (StateEqualityTests).
    /// </summary>
    public struct StatusEffectState : IEquatable<StatusEffectState>
    {
        public bool Active;

        /// <summary>Fighter slot under the effect.</summary>
        public int Target;

        /// <summary>Fighter slot that applied it, or −1.</summary>
        public int Source;

        /// <summary>Team of the source when applied (marks are per team).</summary>
        public int SourceTeam;

        public EffectKind Kind;

        /// <summary>Percent for slow / damage reduction; unused for the other kinds.</summary>
        public int Value;

        /// <summary>First tick the effect no longer applies.</summary>
        public int ExpireTick;

        public bool Equals(StatusEffectState o)
        {
            return Active == o.Active && Target == o.Target && Source == o.Source && SourceTeam == o.SourceTeam
                && Kind == o.Kind && Value == o.Value && ExpireTick == o.ExpireTick;
        }

        public override bool Equals(object obj) => obj is StatusEffectState other && Equals(other);
        public override int GetHashCode() => (Target * 397) ^ ((int)Kind * 31) ^ ExpireTick;
    }
}
