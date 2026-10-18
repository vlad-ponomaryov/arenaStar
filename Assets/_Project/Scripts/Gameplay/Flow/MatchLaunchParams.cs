using Arena.Bots;
using Arena.Core.Modes;

namespace Arena.Gameplay.Flow
{
    /// <summary>
    /// What the menu chose for the next match: the local player's fighter, bot difficulty (all other slots are bots) and the
    /// RNG seed. Immutable; "Play again" reuses the same instance (<see cref="SceneFlow.Restart"/>). <see cref="Online"/> —
    /// the match comes from the server (S3.10b): the Match scene's online bootstrap gives the runner its driver; the fighter and
    /// the bot choice are kept for the menu. <see cref="Mode"/> — the online mode searched (S3.11; offline training is Acorns
    /// until S3.14 adds the mode choice there).
    /// </summary>
    public sealed class MatchLaunchParams
    {
        public readonly string FighterId;
        public readonly BotDifficulty Bots;
        public readonly int Seed;
        public readonly bool Online;
        public readonly ModeKind Mode;

        public MatchLaunchParams(string fighterId, BotDifficulty bots, int seed, bool online = false, ModeKind mode = ModeKind.Acorns)
        {
            FighterId = fighterId;
            Bots = bots;
            Seed = seed;
            Online = online;
            Mode = mode;
        }
    }
}
