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

        /// <summary>Health removed. Source = attacker (or −1), Target = victim, Value = amount removed, Position = victim,
        /// AbilityRef = the ability that dealt it (or −1).</summary>
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
        AreaTriggered = 9,

        /// <summary>Ult became fully charged. Source = fighter slot, Value = charge.</summary>
        UltReady = 10,

        /// <summary>Timed effect applied or refreshed. Source = applier (or −1), Target = affected fighter,
        /// Value = <c>EffectKind</c>, Position = target.</summary>
        StatusApplied = 11,

        /// <summary>Breakable wall destroyed (now floor). Source = breaker (or −1), Value = tile index (row × width + column),
        /// Position = cell centre.</summary>
        TileBroken = 12,

        /// <summary>Acorn or berry appeared. Value = pickup slot, Position = where it lies.</summary>
        PickupSpawned = 13,

        /// <summary>Acorn or berry picked up. Source = fighter, Value = acorns (berries) it was worth, Position = where it lay.</summary>
        PickupCollected = 14,

        /// <summary>A defeated fighter dropped its acorns. Source = fighter, Value = acorns dropped, Position = fighter.</summary>
        AcornsDropped = 15,

        /// <summary>Hold countdown started. Value = team.</summary>
        CountdownStarted = 16,

        /// <summary>Hold countdown reset. Value = team that lost it.</summary>
        CountdownReset = 17,

        /// <summary>Match over. Value = winning team, or −1 for a draw.</summary>
        MatchEnded = 18,

        /// <summary>Berry box hit (Last Beast). Source = attacker (or −1), Value = durability removed, Target = box slot,
        /// Position = cell centre. At 0 durability <see cref="TileBroken"/> follows.</summary>
        BoxDamaged = 19,

        /// <summary>The fog moved in (Last Beast). Value = fog step (1 = first), Position = safe rectangle min corner,
        /// Direction = its max corner.</summary>
        FogAdvanced = 20,

        /// <summary>A fighter is out of a free-for-all match (Last Beast). Source = fighter, Value = its place.</summary>
        FighterPlaced = 21
    }
}
