namespace Arena.Core.Defs
{
    /// <summary>What an effect does to a target (D-011). S2.4 adds heal, knockback, stun, slow, damage reduction, invisibility, mark.</summary>
    public enum EffectKind : byte
    {
        None = 0,

        /// <summary>Removes <see cref="EffectDef.Value"/> health (level-scaled by the source's level).</summary>
        Damage = 1
    }
}
