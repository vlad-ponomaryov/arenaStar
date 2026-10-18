using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Immutable-by-convention description of a fighter for the simulation (D-010): converted from FighterConfig, values
    /// already in cells / ticks / int. Base values are for level 1; level scaling is applied at spawn
    /// (<see cref="CombatRules.ScaleByLevel"/>). Abilities are data (D-011): attack, ult, trick (null = none), talent.
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

        /// <summary>Main attack.</summary>
        public AbilityDef Attack;

        /// <summary>Ult (super), or null. Charged by damage of the main attack up to <see cref="UltChargeRequired"/>.</summary>
        public AbilityDef Ult;

        /// <summary>Ult charge (damage dealt by the attack) needed at level 1; scaled by level like damage.</summary>
        public int UltChargeRequired;

        /// <summary>Trick (active, limited uses), or null.</summary>
        public AbilityDef Trick;

        /// <summary>Passive talent, or null.</summary>
        public TalentDef Talent;

        /// <summary>When bots use the trick (<see cref="Defs.BotTrickHint"/>); ignored by the simulation.</summary>
        public BotTrickHint BotTrickHint;

        /// <summary>Main attack range in cells (also the auto-aim radius, D-007) = <c>Attack.Range</c>.</summary>
        public float AttackRange => Attack != null ? Attack.Range : 0f;

        /// <summary>Ability by slot, or null if the fighter has none there.</summary>
        public AbilityDef GetAbility(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Attack: return Attack;
                case AbilitySlot.Ult: return Ult;
                case AbilitySlot.Trick: return Trick;
                default: return null;
            }
        }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Id)) throw new ArgumentException("FighterDef.Id is empty");
            if (MaxHealth <= 0) throw new ArgumentException("FighterDef " + Id + ": MaxHealth must be > 0");
            if (MoveSpeed < 0f) throw new ArgumentException("FighterDef " + Id + ": MoveSpeed must be ≥ 0");
            if (Radius <= 0f) throw new ArgumentException("FighterDef " + Id + ": Radius must be > 0");
            if (AttackRechargeTicks <= 0) throw new ArgumentException("FighterDef " + Id + ": AttackRechargeTicks must be > 0");
            if (Attack == null) throw new ArgumentException("FighterDef " + Id + ": Attack is null");
            Attack.Validate("FighterDef " + Id + ".Attack");
            if (Ult != null)
            {
                ValidateSingleShot(Ult, "Ult");
                if (UltChargeRequired <= 0) throw new ArgumentException("FighterDef " + Id + ": UltChargeRequired must be > 0");
            }
            if (Trick != null) ValidateSingleShot(Trick, "Trick");
            if (Talent != null) Talent.Validate("FighterDef " + Id + ".Talent");
        }

        // The burst state of a fighter belongs to the main attack (FighterState.BurstShotsLeft), so other slots fire once.
        private void ValidateSingleShot(AbilityDef ability, string slot)
        {
            string owner = "FighterDef " + Id + "." + slot;
            ability.Validate(owner);
            if (ability.BurstCount != 1) throw new ArgumentException(owner + ": only the main attack may have a burst");
        }
    }
}
