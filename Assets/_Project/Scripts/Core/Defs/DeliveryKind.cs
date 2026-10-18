namespace Arena.Core.Defs
{
    /// <summary>
    /// How an ability reaches its targets (D-011). S2.3: straight projectiles and an instant melee cone; S2.4 adds dash,
    /// self area and self buff; S2.11–S2.12 add lob, placed area and hook. Append new kinds at the end.
    /// </summary>
    public enum DeliveryKind : byte
    {
        None = 0,

        /// <summary>Straight projectile(s) along the aim direction, up to <see cref="AbilityDef.Range"/>.</summary>
        Projectile = 1,

        /// <summary>Instant hit of every target inside a cone (<see cref="AbilityDef.ConeAngleDegrees"/>, <see cref="AbilityDef.Range"/>).</summary>
        MeleeCone = 2
    }
}
