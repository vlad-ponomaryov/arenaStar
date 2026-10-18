using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// One fighter in designer units (GDD §6): seconds, cells. Converted to <c>Arena.Core.Defs.FighterDef</c> by
    /// ConfigConverter. Values are written by DataSeeder (D-010). Abilities (attack shape, ult, trick, talent) — S2.3+.
    /// </summary>
    [CreateAssetMenu(fileName = "Fighter", menuName = "Arena/Fighter")]
    public sealed class FighterConfig : ScriptableObject
    {
        [Tooltip("Stable lowercase id: pix, bruno, chomp, kora, nova, vex.")]
        public string Id;

        [Tooltip("Localization key of the display name, e.g. fighter.pix.name.")]
        public string NameKey;

        [Header("Stats at level 1")]
        [Min(1)] public int MaxHealth;
        public SpeedClass Speed;

        [Tooltip("Body radius in cells (PROTO).")]
        [Min(0.01f)] public float Radius;

        [Header("Main attack")]
        [Tooltip("Range in cells (also the auto-aim radius).")]
        [Min(0.1f)] public float AttackRange;

        [Tooltip("Seconds to restore one attack charge.")]
        [Min(0.01f)] public float AttackRechargeSeconds;

        [Tooltip("Damage per hit at level 1.")]
        [Min(0)] public int AttackDamage;
    }
}
