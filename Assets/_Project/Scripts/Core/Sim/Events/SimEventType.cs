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
        ChargeRestored = 2
    }
}
