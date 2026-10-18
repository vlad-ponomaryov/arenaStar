namespace Arena.Core.Defs
{
    /// <summary>
    /// How an ability reaches its targets (D-011). Implemented: projectile (single or fan), melee cone, dash, self area,
    /// self buff. Reserved (rejected by <see cref="AbilityDef.Validate"/> until their steps): lob, placed area, hook
    /// (S2.11–S2.12). Append new kinds at the end — the value is serialized in configs.
    /// </summary>
    public enum DeliveryKind : byte
    {
        /// <summary>No ability in this slot (configs of fighters without an ult/trick).</summary>
        None = 0,

        /// <summary>Straight projectile(s) along the aim direction, up to <see cref="AbilityDef.Range"/>; a fan when
        /// <see cref="AbilityDef.ProjectileCount"/> &gt; 1 (spread over <see cref="AbilityDef.SpreadDegrees"/>).</summary>
        Projectile = 1,

        /// <summary>Instant hit of every target inside a cone (<see cref="AbilityDef.ConeAngleDegrees"/>, <see cref="AbilityDef.Range"/>).</summary>
        MeleeCone = 2,

        /// <summary>The caster moves <see cref="AbilityDef.DashDistance"/> at <see cref="AbilityDef.DashSpeed"/>; every
        /// matching fighter its body touches on the way gets the effects once (no effects = a pure movement trick).</summary>
        Dash = 3,

        /// <summary>Instant circle of radius <see cref="AbilityDef.Range"/> around the caster (may include the caster).</summary>
        SelfArea = 4,

        /// <summary>Effects applied to the caster only (shield, invisibility…).</summary>
        SelfBuff = 5,

        /// <summary>Reserved (S2.11): arcing projectile over walls that creates a delayed area at the aim point.</summary>
        Lob = 6,

        /// <summary>Reserved (S2.11): an area placed at the caster position (mine).</summary>
        PlaceArea = 7,

        /// <summary>Reserved (S2.12): hook that pulls the caster to a wall or a target.</summary>
        Hook = 8
    }
}
