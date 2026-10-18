namespace Arena.Core.Defs
{
    /// <summary>
    /// When a talent modifier is active (D-011), evaluated on demand from the current state (no stored talent state).
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

        /// <summary>Against targets marked by the fighter's team (<see cref="EffectKind.Mark"/>; Nova, with
        /// <see cref="TalentModifier.DamageMul"/> only).</summary>
        TargetMarked = 3,

        /// <summary>Triggered when the fighter defeats an enemy (Vex, with <see cref="TalentModifier.RefillCharges"/> only).</summary>
        OnKill = 4,

        /// <summary>Aura (Kora): affects allies (not the owner) within <see cref="TalentDef.Param"/> cells of the owner.</summary>
        AllyAura = 5
    }
}
