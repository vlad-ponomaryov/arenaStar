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

        /// <summary>Reserved (Chomp, S2.11): area radius +Value %.</summary>
        AreaRadiusMul = 3,

        /// <summary>Reserved (Kora, S2.11): regeneration delay of allies.</summary>
        RegenDelay = 4,

        /// <summary>Reserved (Vex, S2.12): refill all attack charges.</summary>
        RefillCharges = 5
    }
}
