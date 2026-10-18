using System;
using Arena.Core.Defs;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>One effect of an ability in designer units (seconds, cells); converted to <see cref="EffectDef"/>.</summary>
    [Serializable]
    public sealed class EffectConfig
    {
        public EffectKind Kind = EffectKind.Damage;
        public TargetFilter Filter = TargetFilter.Enemy;

        [Tooltip("Damage/heal at level 1; percent for Slow, DamageReduction and BoostShots.")]
        [Min(0)] public int Value;

        [Tooltip("Seconds: duration of stun/slow/reduction/invisibility/mark/team reveal; push time of a knockback.")]
        [Min(0f)] public float DurationSeconds;

        [Tooltip("Cells, for knockback.")]
        [Min(0f)] public float Distance;

        [Tooltip("Boosted attack activations, for BoostShots.")]
        [Min(0)] public int Count;
    }
}
