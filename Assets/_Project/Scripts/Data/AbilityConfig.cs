using System;
using Arena.Core.Defs;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// An ability in designer units (seconds, cells), serialized inside <see cref="FighterConfig"/> (D-011).
    /// Converted to <see cref="AbilityDef"/> by ConfigConverter. Values are written by DataSeeder (D-010).
    /// </summary>
    [Serializable]
    public sealed class AbilityConfig
    {
        public DeliveryKind Delivery = DeliveryKind.Projectile;

        [Tooltip("Cells: projectile flight distance or cone radius. For the attack also the auto-aim radius.")]
        [Min(0.1f)] public float Range;

        [Header("Projectile")]
        [Tooltip("Cells per second (PROTO).")]
        [Min(0f)] public float ProjectileSpeed;

        [Tooltip("Hit radius in cells, added to the target's body radius (PROTO).")]
        [Min(0f)] public float ProjectileRadius;

        [Tooltip("Extra targets passed through after the first hit.")]
        [Min(0)] public int PierceCount;

        [Header("Cone")]
        [Tooltip("Full angle in degrees.")]
        [Range(0f, 360f)] public float ConeAngleDegrees;

        [Header("Burst")]
        [Tooltip("Shots per activation; one charge pays for the whole burst.")]
        [Min(1)] public int BurstCount = 1;

        [Tooltip("Seconds between shots of a burst (PROTO).")]
        [Min(0f)] public float BurstIntervalSeconds;

        public EffectConfig[] Effects = new EffectConfig[0];
    }
}
