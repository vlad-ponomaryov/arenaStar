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

        [Tooltip("Damage/heal at level 1; percent for multipliers (S2.4).")]
        [Min(0)] public int Value;

        [Tooltip("Seconds, for timed effects (S2.4).")]
        [Min(0f)] public float DurationSeconds;

        [Tooltip("Cells, for knockback-like effects (S2.4).")]
        [Min(0f)] public float Distance;
    }
}
