using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Random;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Sim
{
    /// <summary>
    /// Deterministic fixed-step match simulation (D-001): <see cref="Tick"/> advances the state by 1/30 s.
    /// The same code runs offline (MatchRunner), in tests and — on stage 3 — inside a Fusion NetworkBehaviour (D-038).
    ///
    /// Order of systems in a tick (fixed; new systems are inserted at their documented place):
    ///   0. events cleared;
    ///   1. input: sanitize, latch buttons (PrevButtons ← Buttons ← input) for rising-edge detection;
    ///   2. movement;            (S2.3+: targeting → attacks → projectiles → areas → damage → respawn)
    ///   3. attack charges;
    ///   4. regeneration;
    ///   5. Tick++.
    ///
    /// No allocations per tick: the state, the event buffer and the input scratch array are allocated once here.
    /// </summary>
    public sealed class MatchSimulation
    {
        private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];

        public MatchDefs Defs { get; }
        public MatchSetup Setup { get; }
        public MatchState State { get; }

        /// <summary>Events of the last tick (or of <see cref="Reset"/> before the first tick).</summary>
        public SimEventBuffer Events { get; }

        public int FighterCount => State.Globals.FighterCount;

        public MatchSimulation(MatchDefs defs, MatchSetup setup)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            defs.Validate();
            setup.Validate(defs);

            Defs = defs;
            Setup = setup;
            State = new MatchState();
            Events = new SimEventBuffer();
            Reset();
        }

        /// <summary>Restarts the match from tick 0 with the setup's seed and spawn points.</summary>
        public void Reset()
        {
            State.Clear();
            Events.Clear();
            ref MatchGlobals g = ref State.Globals;
            g.Seed = Setup.Seed;
            g.Rng = DeterministicRng.FromSeed(Setup.Seed);
            g.FighterCount = Setup.SlotCount;
            for (int i = 0; i < Setup.SlotCount; i++)
                SpawnSystem.SpawnFighter(State, Defs, in Setup.Slots[i], i, Events);
        }

        /// <param name="inputs">Input per fighter slot (index = slot); length ≥ <see cref="FighterCount"/>.</param>
        public void Tick(PlayerInput[] inputs)
        {
            int count = State.Globals.FighterCount;
            if (inputs == null || inputs.Length < count)
                throw new ArgumentException("MatchSimulation.Tick: need " + count + " inputs");

            Events.Clear();
            ApplyInputs(inputs, count);
            MovementSystem.Run(State, Defs, _inputs);
            ChargeSystem.Run(State, Defs, Events);
            RegenSystem.Run(State, Defs);
            State.Globals.Tick++;
        }

        private void ApplyInputs(PlayerInput[] inputs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref State.Fighters[i];
                PlayerInput input = f.Active && f.Alive ? inputs[i].Sanitized() : default;
                _inputs[i] = input;
                f.PrevButtons = f.Buttons;
                f.Buttons = input.Buttons;
            }
        }
    }
}
