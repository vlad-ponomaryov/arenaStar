namespace Arena.Core.Defs
{
    /// <summary>
    /// When a talent modifier is active (D-011), evaluated on demand from the current state (no stored talent state).
    /// Reserved kinds are rejected by <see cref="TalentDef.Validate"/> until their fighters' steps (S2.11–S2.12).
    /// Append new kinds at the end.
    /// </summary>
    public enum TalentCondition : byte
    {
        /// <summary>Always active.</summary>
        Always = 0,

        /// <summary>While the ult is fully charged (Pix).</summary>
        UltCharged = 1,

        /// <summary>While health is below <see cref="TalentDef.Param"/> % of max (Bruno).</summary>
        HealthBelow = 2,

        /// <summary>Reserved (Nova, S2.12): against targets marked by the fighter team.</summary>
        TargetMarked = 3,

        /// <summary>Reserved (Vex, S2.12): triggered by defeating an enemy.</summary>
        OnKill = 4,

        /// <summary>Aura (Kora): affects allies (not the owner) within <see cref="TalentDef.Param"/> cells of the owner.</summary>
        AllyAura = 5
    }
}
