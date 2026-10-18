using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Immutable-by-convention description of a fighter for the simulation (D-010): converted from FighterConfig, values
    /// already in cells / ticks / int. Base values are for level 1; level scaling is applied at spawn
    /// (<see cref="CombatRules.ScaleByLevel"/>). Abilities are added in S2.3–S2.4.
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

        /// <summary>Main attack range in cells (also the auto-aim radius, D-007).</summary>
        public float AttackRange;

        /// <summary>Ticks to restore one attack charge.</summary>
        public int AttackRechargeTicks;

        /// <summary>Main attack damage per hit at level 1 (used from S2.3).</summary>
        public int AttackDamage;

        public void Validate()
        {
            if (string.IsNullOrEmpty(Id)) throw new ArgumentException("FighterDef.Id is empty");
            if (MaxHealth <= 0) throw new ArgumentException("FighterDef " + Id + ": MaxHealth must be > 0");
            if (MoveSpeed < 0f) throw new ArgumentException("FighterDef " + Id + ": MoveSpeed must be ≥ 0");
            if (Radius <= 0f) throw new ArgumentException("FighterDef " + Id + ": Radius must be > 0");
            if (AttackRange <= 0f) throw new ArgumentException("FighterDef " + Id + ": AttackRange must be > 0");
            if (AttackRechargeTicks <= 0) throw new ArgumentException("FighterDef " + Id + ": AttackRechargeTicks must be > 0");
            if (AttackDamage < 0) throw new ArgumentException("FighterDef " + Id + ": AttackDamage must be ≥ 0");
        }
    }
}
