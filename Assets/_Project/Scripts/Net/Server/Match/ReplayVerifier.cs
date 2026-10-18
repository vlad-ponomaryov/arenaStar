using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Sim;
using Arena.Core.State;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// Replays a match from its <see cref="InputLog"/> (Server-Spec §5.3, §11.1): a fresh <see cref="MatchSimulation"/> with the
    /// same defs and setup, tick by tick with the logged inputs, then <see cref="MatchState.ContentEquals"/> against the state
    /// the server ended with. A mismatch means the server's tick depended on something besides its inputs. Allocates (diagnostics).
    /// </summary>
    public static class ReplayVerifier
    {
        public static bool Verify(MatchDefs defs, MatchSetup setup, InputLog log, MatchState expected, out string error)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (log.Overflowed)
            {
                error = "input log overflowed at " + log.CapacityTicks + " ticks";
                return false;
            }
            var sim = new MatchSimulation(defs, setup);
            var inputs = new PlayerInput[SimLimits.MaxFighters];
            for (int t = 0; t < log.Ticks; t++)
            {
                log.CopyTick(t, inputs);
                sim.Tick(inputs);
            }
            if (sim.State.Globals.Tick != expected.Globals.Tick)
            {
                error = "replay ended at tick " + sim.State.Globals.Tick + ", server at " + expected.Globals.Tick;
                return false;
            }
            if (!sim.State.ContentEquals(expected))
            {
                error = "replayed state differs from the server's at tick " + expected.Globals.Tick;
                return false;
            }
            error = null;
            return true;
        }
    }
}
