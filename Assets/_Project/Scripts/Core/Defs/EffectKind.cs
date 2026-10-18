namespace Arena.Core.Defs
{
    /// <summary>
    /// What an effect does to a target (D-011). Each kind has one small static handler in <c>Arena.Core.Sim.Effects</c>,
    /// dispatched by <c>EffectSystem</c>. Timed kinds become entries of <c>MatchState.Statuses</c>. Append new kinds at the end.
    /// </summary>
    public enum EffectKind : byte
    {
        None = 0,

        /// <summary>Removes <see cref="EffectDef.Value"/> health (level-scaled by the source level, × source talent,
        /// × target damage-taken multiplier).</summary>
        Damage = 1,

        /// <summary>Restores <see cref="EffectDef.Value"/> health (level-scaled by the source level).</summary>
        Heal = 2,

        /// <summary>Pushes the target <see cref="EffectDef.Distance"/> cells over <see cref="EffectDef.DurationTicks"/>
        /// along the delivery direction (projectile flight, dash) or away from the origin (cone, area).</summary>
        Knockback = 3,

        /// <summary>For <see cref="EffectDef.DurationTicks"/> the target neither moves nor uses abilities; cancels its burst and dash.</summary>
        Stun = 4,

        /// <summary>Movement speed −<see cref="EffectDef.Value"/> % for the duration (the strongest of several applies).</summary>
        Slow = 5,

        /// <summary>Damage taken −<see cref="EffectDef.Value"/> % for the duration (the strongest of several applies).</summary>
        DamageReduction = 6,

        /// <summary>Hidden from enemies at any distance for the duration; removed when the fighter attacks (D-024; S2.5 reads it).</summary>
        Invisibility = 7,

        /// <summary>Visible to the source team for the duration, even in bushes (D-024; S2.5 reads it).</summary>
        Mark = 8
    }
}
