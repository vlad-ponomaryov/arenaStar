namespace Arena.Core.Modes
{
    /// <summary>Phase of a match. After <see cref="Ended"/> the simulation stands still and ignores input.</summary>
    public enum ModePhase : byte
    {
        Playing = 0,
        Ended = 1
    }
}
