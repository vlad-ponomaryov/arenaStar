using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// One fighter in designer units (GDD §6): seconds, cells. Converted to <c>Arena.Core.Defs.FighterDef</c> by
    /// ConfigConverter. Values are written by DataSeeder (D-010). Abilities are data (D-011): an ult or trick with
    /// delivery <c>None</c> means the fighter has none; a talent with modifier <c>None</c> — no talent.
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
        [Tooltip("Seconds to restore one attack charge.")]
        [Min(0.01f)] public float AttackRechargeSeconds;

        [Tooltip("Delivery, range and effects (damage per hit at level 1).")]
        public AbilityConfig Attack = new AbilityConfig();

        [Header("Ult")]
        [Tooltip("Damage dealt by the main attack needed to charge the ult, at level 1 (PROTO).")]
        [Min(0)] public int UltChargeRequired;

        public AbilityConfig Ult = new AbilityConfig { Delivery = Arena.Core.Defs.DeliveryKind.None };

        [Header("Trick and talent")]
        public AbilityConfig Trick = new AbilityConfig { Delivery = Arena.Core.Defs.DeliveryKind.None };

        public TalentConfig Talent = new TalentConfig();
    }
}
