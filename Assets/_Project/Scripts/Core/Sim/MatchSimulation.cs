using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Modes;
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
    ///   2. statuses: drop expired, recompute aggregates (stun, slow, damage reduction, invisibility, marks);
    ///   3. movement (knockback &gt; stun &gt; dash &gt; input; slow and talent speed);
    ///   4. abilities per fighter: attack (burst / press), ult, trick — aim resolution, then delivery: projectiles
    ///      spawned, cones / self areas / self buffs applied at once, dashes started;
    ///   5. dashes (move, hit touched fighters);
    ///   6. projectiles (move, hit, apply effects);
    ///   7. areas (delayed zones);
    ///      effects, damage, healing, ult charge and defeat happen inside 4–7 (DamageSystem is the single health point);
    ///   8. respawn;
    ///   8a. mode rules (Acorns: acorns from the hollow, pickup, hold countdown, time limit — AcornsRules);
    ///   9. attack charges;
    ///  10. regeneration;
    ///  11. visibility (bushes, reveal, invisibility, marks; read by views, bots and next tick's auto-aim);
    ///  12. Tick++.
    /// Once the mode has ended (<see cref="ModePhase.Ended"/>) Tick only clears the events: input is ignored and the state
    /// stands still (the results screen reads it).
    /// Movement, dashes and projectiles collide with the mutable map tiles in the state (walls break there).
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
            TileSystem.Load(State, Defs.Map);
            for (int i = 0; i < Setup.SlotCount; i++)
                SpawnSystem.SpawnFighter(State, Defs, Setup, i, Events);
            AcornsRules.Reset(State, Defs);
            VisibilitySystem.Run(State, Defs);
        }

        /// <summary>The mode has ended; further ticks do nothing.</summary>
        public bool IsEnded => State.Mode.IsEnded;

        /// <param name="inputs">Input per fighter slot (index = slot); length ≥ <see cref="FighterCount"/>.</param>
        public void Tick(PlayerInput[] inputs)
        {
            int count = State.Globals.FighterCount;
            if (inputs == null || inputs.Length < count)
                throw new ArgumentException("MatchSimulation.Tick: need " + count + " inputs");

            Events.Clear();
            if (State.Mode.IsEnded) return;
            ApplyInputs(inputs, count);
            StatusSystem.Run(State);
            MovementSystem.Run(State, Defs, _inputs);
            AbilitySystem.Run(State, Defs, _inputs, Events);
            DashSystem.Run(State, Defs, Events);
            ProjectileSystem.Run(State, Defs, Events);
            AreaSystem.Run(State, Defs, Events);
            RespawnSystem.Run(State, Defs, Setup, Events);
            AcornsRules.Run(State, Defs, Events);
            ChargeSystem.Run(State, Defs, Events);
            RegenSystem.Run(State, Defs);
            VisibilitySystem.Run(State, Defs);
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
