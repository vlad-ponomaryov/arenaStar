namespace Arena.Core.Defs
{
    /// <summary>
    /// What a talent changes while its condition holds (D-011); <see cref="TalentDef.Value"/> is in percent.
    /// Reserved kinds are rejected by <see cref="TalentDef.Validate"/> until S2.11–S2.12. Append new kinds at the end.
    /// </summary>
    public enum TalentModifier : byte
    {
        /// <summary>No talent (configs of fighters without one).</summary>
        None = 0,

        /// <summary>Movement speed +Value %.</summary>
        MoveSpeedMul = 1,

        /// <summary>Damage dealt +Value %.</summary>
        DamageMul = 2,

        /// <summary>Radius of the areas the fighter creates (lob, placed) +Value % (Chomp).</summary>
        AreaRadiusMul = 3,

        /// <summary>Regeneration delay of the allies in the aura becomes <see cref="TalentDef.Ticks"/> (Kora; with
        /// <see cref="TalentCondition.AllyAura"/> only).</summary>
        RegenDelay = 4,

        /// <summary>Reserved (Vex, S2.12): refill all attack charges.</summary>
        RefillCharges = 5
    }
}
