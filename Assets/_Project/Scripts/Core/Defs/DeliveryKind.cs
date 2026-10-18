namespace Arena.Core.Defs
{
    /// <summary>
    /// How an ability reaches its targets (D-011). Implemented: projectile (single or fan, pierce, bounce), melee cone, dash,
    /// self area, self buff, lob, placed area (S2.11), hook (S2.12). Append new kinds at the end — the value is serialized
    /// in configs.
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

        /// <summary>Arc over walls: a <see cref="AreaKind.Burst"/> area of <see cref="AbilityDef.AreaRadius"/> lands at
        /// <c>direction × aim length × Range</c> (auto-aim: at the target, no aim: full range) and triggers
        /// <see cref="AbilityDef.AreaDelayTicks"/> later (the flight time; telegraphed to everyone).</summary>
        Lob = 6,

        /// <summary>An area of <see cref="AbilityDef.PlacedArea"/> kind placed at the caster position (mine, dome).</summary>
        PlaceArea = 7,

        /// <summary>A projectile (as <see cref="Projectile"/>, single, no pierce/bounce) that pulls its caster at
        /// <see cref="AbilityDef.DashSpeed"/>: hitting a wall — to the wall; hitting an enemy — to the enemy (next to its body),
        /// the effects apply to the enemy at the hit. A miss does nothing (Vex's "Hook").</summary>
        Hook = 8
    }
}
