using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Immutable-by-convention description of a fighter for the simulation (D-010): converted from FighterConfig, values
    /// already in cells / ticks / int. Base values are for level 1; level scaling is applied at spawn
    /// (<see cref="CombatRules.ScaleByLevel"/>). Abilities: the attack (S2.3); ult, trick, talent — S2.4.
    /// </summary>
    public sealed class FighterDef
    {
        /// <summary>Stable id (<c>pix</c>, <c>bruno</c>, …). Not used inside the tick.</summary>
        public string Id;

        /// <summary>Localization key of the display name (<c>fighter.pix.name</c>).</summary>
        public string NameKey;

        /// <summary>Health at level 1.</summary>
        public int MaxHealth;

        /// <summary>Movement speed, cells per second (resolved from the speed class by the converter).</summary>
        public float MoveSpeed;

        /// <summary>Collision / hit radius in cells.</summary>
        public float Radius;

        /// <summary>Ticks to restore one attack charge.</summary>
        public int AttackRechargeTicks;

        /// <summary>Main attack (S2.3); ult and trick come in S2.4.</summary>
        public AbilityDef Attack;

        /// <summary>Main attack range in cells (also the auto-aim radius, D-007) = <c>Attack.Range</c>.</summary>
        public float AttackRange => Attack != null ? Attack.Range : 0f;

        /// <summary>Ability by slot, or null if the fighter has none there.</summary>
        public AbilityDef GetAbility(AbilitySlot slot) => slot == AbilitySlot.Attack ? Attack : null;

        public void Validate()
        {
            if (string.IsNullOrEmpty(Id)) throw new ArgumentException("FighterDef.Id is empty");
            if (MaxHealth <= 0) throw new ArgumentException("FighterDef " + Id + ": MaxHealth must be > 0");
            if (MoveSpeed < 0f) throw new ArgumentException("FighterDef " + Id + ": MoveSpeed must be ≥ 0");
            if (Radius <= 0f) throw new ArgumentException("FighterDef " + Id + ": Radius must be > 0");
            if (AttackRechargeTicks <= 0) throw new ArgumentException("FighterDef " + Id + ": AttackRechargeTicks must be > 0");
            if (Attack == null) throw new ArgumentException("FighterDef " + Id + ": Attack is null");
            Attack.Validate("FighterDef " + Id + ".Attack");
        }
    }
}
