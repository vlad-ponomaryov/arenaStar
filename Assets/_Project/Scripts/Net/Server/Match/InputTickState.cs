namespace Arena.Net.Server.Match
{
    /// <summary>State of one tick in a <see cref="SlotInputBuffer"/> ring.</summary>
    public enum InputTickState : byte
    {
        Empty = 0,

        /// <summary>A real input arrived and waits for its tick.</summary>
        Received = 1,

        /// <summary>The tick was simulated with the real input (later copies are duplicates).</summary>
        Applied = 2,

        /// <summary>The tick was simulated without a real input (repeat or zero): a later copy is "late".</summary>
        Missed = 3,

        /// <summary>A late copy of a missed tick was handled (its presses carried over); further copies are ignored.</summary>
        LateHandled = 4,

        /// <summary>A countdown tick (&lt; 0): consumed, never applied (§2.3).</summary>
        Skipped = 5,
    }
}
