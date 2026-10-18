using System;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Sim;

namespace Arena.Net.Client
{
    /// <summary>
    /// A bot in place of the player of a match client (tests, the server self-test, Server-Spec §11.1–11.2): the offline
    /// <see cref="BotController"/> on the client's own slot reads only the display state (<see cref="PresentationState"/>) — what
    /// a human would see: foreign fighters where they are shown, hidden enemies hidden — and returns the raw input the session
    /// quantizes, predicts and sends. No allocations after construction.
    /// </summary>
    public sealed class ClientBot
    {
        private readonly BotController _bots;
        private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];

        public ClientBot(BotBehaviourDef behaviour, BotDifficultyDef difficulty, int slot, int seed)
        {
            if (behaviour == null) throw new ArgumentNullException(nameof(behaviour));
            if ((uint)slot >= SimLimits.MaxFighters) throw new ArgumentOutOfRangeException(nameof(slot));
            Slot = slot;
            _bots = new BotController(behaviour);
            _bots.AddBot(slot, difficulty, seed);
        }

        public int Slot { get; }

        public BotController Controller => _bots;

        /// <summary>The input for the next prediction tick from the last display tick (zero before the first one).</summary>
        public PlayerInput Think(PresentationState presentation, MatchDefs defs)
        {
            if (presentation == null || defs == null || !presentation.HasState) return default;
            _bots.Update(presentation.State, defs, presentation.Events, _inputs);
            return _inputs[Slot];
        }
    }
}
