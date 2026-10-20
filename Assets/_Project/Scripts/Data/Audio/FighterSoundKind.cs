namespace Arena.Data.Audio
{
    /// <summary>Cues of a <see cref="FighterSoundSet"/>, in the order of <see cref="FighterSoundSet.Cue"/>.</summary>
    public enum FighterSoundKind : byte
    {
        Step = 0,
        Shot = 1,
        Hit = 2,
        Ult = 3,
        Hurt = 4,
        VoEffort = 5,
        VoHurt = 6,
        VoWin = 7,
        VoLose = 8
    }
}
