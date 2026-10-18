namespace Arena.Core.Sim.Events
{
    /// <summary>
    /// Kinds of tick events for visual feedback (D-008). Append new kinds at the end (hit, death, respawn, shot, pickup,
    /// tile broken, ult ready… arrive with their systems in S2.3+).
    /// </summary>
    public enum SimEventType : byte
    {
        None = 0,

        /// <summary>Fighter appeared. Source = fighter slot, Position = spawn point.</summary>
        FighterSpawned = 1,

        /// <summary>One attack charge restored. Source = fighter slot, Value = charges now.</summary>
        ChargeRestored = 2,

        /// <summary>One shot of an ability (each drop of a burst, each cone swing). Source = fighter, Value = ability ref,
        /// Position = origin, Direction = unit aim.</summary>
        AbilityFired = 3,

        /// <summary>Health removed. Source = attacker (or −1), Target = victim, Value = amount removed, Position = victim.</summary>
        Damaged = 4,

        /// <summary>Health restored. Source = healer, Target = healed, Value = amount restored, Position = target.</summary>
        Healed = 5,

        /// <summary>Fighter defeated. Source = attacker (or −1), Target = victim, Value = respawn tick, Position = victim.</summary>
        FighterDefeated = 6,

        /// <summary>Projectile hit a target (and may continue if piercing). Source = owner, Target = fighter hit,
        /// Value = projectile slot, Position = hit point.</summary>
        ProjectileHit = 7,

        /// <summary>Projectile slot freed (hit or out of range). Source = owner, Value = projectile slot, Position = last point.</summary>
        ProjectileEnded = 8,

        /// <summary>Area applied its effects. Source = owner, Value = area slot, Position = centre.</summary>
        AreaTriggered = 9
    }
}
