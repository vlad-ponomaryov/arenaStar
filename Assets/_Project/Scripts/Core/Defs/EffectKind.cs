namespace Arena.Core.Defs
{
    /// <summary>
    /// What an effect does to a target (D-011). Each kind has one small static handler in <c>Arena.Core.Sim.Effects</c>,
    /// dispatched by <c>EffectSystem</c>. Timed kinds become entries of <c>MatchState.Statuses</c> (the team reveal is a
    /// per-team timer, the shot boost a counter of the fighter). Append new kinds at the end.
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

        /// <summary>Visible to the source team for the duration, even in bushes (D-024). Talents with
        /// <see cref="TalentCondition.TargetMarked"/> deal more damage to marked targets.</summary>
        Mark = 8,

        /// <summary>For the duration the source team sees every enemy on the map, bushes and invisibility included (Nova's
        /// "Owl Eye", D-024). Team-wide (<c>MatchState.TeamRevealUntilTick</c>): survives the source's defeat. Use with the
        /// Self filter.</summary>
        TeamReveal = 9,

        /// <summary>The next <see cref="EffectDef.Count"/> attack activations of the target deal +<see cref="EffectDef.Value"/> %
        /// damage (Nova's "Owl Eye"); replaces an earlier boost; lost on defeat.</summary>
        BoostShots = 10
    }
}
