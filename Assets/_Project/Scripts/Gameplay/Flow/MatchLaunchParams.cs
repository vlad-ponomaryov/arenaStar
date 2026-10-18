using Arena.Bots;

namespace Arena.Gameplay.Flow
{
    /// <summary>
    /// What the menu chose for the next match: the local player's fighter, bot difficulty (all other slots are bots) and the
    /// RNG seed. Immutable; "Play again" reuses the same instance (<see cref="SceneFlow.Restart"/>).
    /// </summary>
    public sealed class MatchLaunchParams
    {
        public readonly string FighterId;
        public readonly BotDifficulty Bots;
        public readonly int Seed;

        public MatchLaunchParams(string fighterId, BotDifficulty bots, int seed)
        {
            FighterId = fighterId;
            Bots = bots;
            Seed = seed;
        }
    }
}
