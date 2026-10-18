namespace Arena.Bots
{
    /// <summary>Bot skill levels (GDD §7). Stage 2 plays Novice and Easy (menu choice); the bots themselves arrive in S2.8–S2.9.</summary>
    public enum BotDifficulty : byte
    {
        Novice = 0,
        Easy = 1,
        Medium = 2,
        Hard = 3
    }
}
