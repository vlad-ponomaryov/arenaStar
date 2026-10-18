namespace Arena.Core.Defs
{
    /// <summary>
    /// Behaviour of an area created by a <see cref="DeliveryKind.Lob"/> or <see cref="DeliveryKind.PlaceArea"/> ability
    /// (stored in <c>AreaState.Kind</c>). Append new kinds at the end — the value is serialized in configs.
    /// </summary>
    public enum AreaKind : byte
    {
        /// <summary>Applies the effects once at its activation tick to everyone matching inside (lobbed bomb), then disappears.</summary>
        Burst = 0,

        /// <summary>From its activation until it expires, the first enemy body touching it sets it off: the effects apply once to
        /// everyone matching inside, then it disappears (Chomp's mine). Expires unused without effects.</summary>
        Mine = 1,

        /// <summary>Lasts <see cref="AbilityDef.AreaDurationTicks"/> and applies its effects every <see cref="AbilityDef.AreaPulseTicks"/>
        /// to everyone matching inside; damage/heal values are the total over the whole duration (Kora's dome). May block
        /// enemy projectiles from outside (<see cref="AbilityDef.BlocksEnemyProjectiles"/>).</summary>
        Field = 2
    }
}
