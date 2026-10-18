using System;
using Arena.Core.Defs;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// An ability in designer units (seconds, cells), serialized inside <see cref="FighterConfig"/> (D-011): delivery +
    /// its parameters + effects. <see cref="DeliveryKind.None"/> = no ability in this slot. Converted to
    /// <see cref="AbilityDef"/> by ConfigConverter. Values are written by DataSeeder (D-010).
    /// </summary>
    [Serializable]
    public sealed class AbilityConfig
    {
        public DeliveryKind Delivery = DeliveryKind.Projectile;

        [Tooltip("Cells: projectile flight distance, cone radius or self-area radius. Also the auto-aim radius of the ability.")]
        [Min(0f)] public float Range;

        [Header("Projectile")]
        [Tooltip("Cells per second (PROTO).")]
        [Min(0f)] public float ProjectileSpeed;

        [Tooltip("Hit radius in cells, added to the target's body radius (PROTO).")]
        [Min(0f)] public float ProjectileRadius;

        [Tooltip("Projectiles per shot; more than 1 = a fan.")]
        [Min(1)] public int ProjectileCount = 1;

        [Tooltip("Full fan angle between the first and the last projectile, degrees.")]
        [Range(0f, 360f)] public float SpreadDegrees;

        [Tooltip("Extra targets passed through after the first hit.")]
        [Min(0)] public int PierceCount;

        [Tooltip("Passes through every target (ignores PierceCount).")]
        public bool PierceAll;

        [Tooltip("Jumps to a new target after a hit, up to this many times (3 targets = 2).")]
        [Min(0)] public int BounceCount;

        [Tooltip("Cells: bounce search radius from the hit point and flight distance after it (PROTO).")]
        [Min(0f)] public float BounceRange;

        [Tooltip("View of the projectile (view only).")]
        public ProjectileViewKind ProjectileView;

        [Header("Cone")]
        [Tooltip("Full angle in degrees.")]
        [Range(0f, 360f)] public float ConeAngleDegrees;

        [Header("Burst (main attack only)")]
        [Tooltip("Shots per activation; one charge pays for the whole burst.")]
        [Min(1)] public int BurstCount = 1;

        [Tooltip("Seconds between shots of a burst (PROTO).")]
        [Min(0f)] public float BurstIntervalSeconds;

        [Header("Dash")]
        [Tooltip("Dash length in cells.")]
        [Min(0f)] public float DashDistance;

        [Tooltip("Dash speed, cells per second (PROTO).")]
        [Min(0f)] public float DashSpeed;

        [Header("Flags")]
        [Tooltip("Breaks breakable walls on its way (walls: S2.5).")]
        public bool BreaksWalls;

        [Tooltip("Passes over walls (walls: S2.5).")]
        public bool IgnoresWalls;

        [Tooltip("Direction = movement stick (else facing) instead of the aim.")]
        public bool UsesMoveDirection;

        [Header("Area (Lob, PlaceArea)")]
        [Tooltip("Cells: radius of the area (explosion, mine, dome).")]
        [Min(0f)] public float AreaRadius;

        [Tooltip("Seconds from the activation to the area being live: lob flight (≥ 0.5 s telegraph), arming of a placed area.")]
        [Min(0f)] public float AreaDelaySeconds;

        [Tooltip("Seconds a placed mine / field lasts after it is live.")]
        [Min(0f)] public float AreaDurationSeconds;

        [Tooltip("Seconds between the effect pulses of a field (damage/heal values are totals over the duration).")]
        [Min(0f)] public float AreaPulseSeconds;

        [Tooltip("Kind of the placed area (a lob is always a burst).")]
        public AreaKind PlacedArea;

        [Tooltip("A placed field stops enemy projectiles coming from outside.")]
        public bool BlocksEnemyProjectiles;

        [Tooltip("Visible to enemies only from close by (bush reveal distance), D-030.")]
        public bool HiddenFromEnemies;

        public EffectConfig[] Effects = new EffectConfig[0];
    }
}
