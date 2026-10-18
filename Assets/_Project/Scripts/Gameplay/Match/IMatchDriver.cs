using Arena.Core.Defs;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Gameplay.Input;

namespace Arena.Gameplay.Match
{
    /// <summary>
    /// Source of the match ticks for <see cref="MatchRunner"/> (Server-Spec §12.2): a "tick by tick" contract, exactly like the
    /// offline loop. Per frame the runner calls <see cref="BeginFrame"/> and then <see cref="Step"/> that many times; after every
    /// step it feeds <see cref="Events"/> and <see cref="State"/> to the views and the HUD. Offline — <see cref="OfflineMatchDriver"/>
    /// (simulation + bots); online (S3.10b) — the presentation state of the client session.
    /// </summary>
    public interface IMatchDriver
    {
        MatchDefs Defs { get; }

        /// <summary>Slots of the match (fighters, teams, spawns).</summary>
        MatchSetup Setup { get; }

        /// <summary>State to show after the last <see cref="Step"/> (before the first step — the initial state).</summary>
        MatchState State { get; }

        /// <summary>Events of the last <see cref="Step"/>.</summary>
        SimEventBuffer Events { get; }

        int LocalSlot { get; }
        int LocalTeam { get; }

        /// <summary>The mode has ended: no more steps, the HUD shows the results.</summary>
        bool IsEnded { get; }

        /// <summary>Fraction of a tick since the last step, for the view interpolation (valid after <see cref="BeginFrame"/>).</summary>
        float Alpha { get; }

        /// <summary>Advances the driver's clock by a frame; returns how many ticks to <see cref="Step"/> in this frame
        /// (at most the configured ticks per frame).</summary>
        int BeginFrame(float deltaTime);

        /// <summary>One tick: local input → tick → new <see cref="State"/> and <see cref="Events"/>.</summary>
        void Step(LocalInputCollector input);
    }
}
