using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Lag compensation of instant contact hits (Server-Spec §2.8, D-072, D-085, D-104): a cone (<c>MeleeCone</c>), a circle
    /// around the caster (<c>SelfArea</c>) and dash touches (<c>Dash</c>) — and the auto-aim of these deliveries — test the
    /// <b>other</b> fighters where the attacker saw them: <c>PlayerInput.ViewDelayTicks</c> ticks back, from
    /// <see cref="MatchState.PositionHistory"/>. Projectiles, lobs and mines are never rewound. The state is not replaced: walls,
    /// domes, effects, events, knockback and dropped acorns use the current state; only the hit tests and the target choice read
    /// <see cref="TargetPosition"/>.
    /// <para>Rewind d means: the target is where it was at the end of tick T − d (T — the tick being simulated), i.e. where the
    /// snapshot of tick T + 1 − d showed it. d is bounded by the history (<see cref="SimLimits.MaxRewindTicks"/>) and by T; the
    /// server limits it further (<c>LagCompMaxRewindMs</c> and its estimate of the client's delay). A target that was not in
    /// play at some tick of the window (died, respawned) is tested without rewind. Prediction (<see cref="MatchGlobals.Predicting"/>)
    /// never rewinds: the client's shadow already holds the shown positions. Offline and bots send 0 — behaviour unchanged.</para>
    /// No allocations.
    /// </summary>
    public static class LagCompensation
    {
        /// <summary>Remembers the position and "alive" of every slot at the end of the current tick (once per tick, before Tick++).</summary>
        public static void Record(MatchState state)
        {
            int tick = state.Globals.Tick;
            if (tick < 0) return;
            int row = tick % SimLimits.MaxRewindTicks;
            int count = state.Globals.FighterCount;
            int baseIndex = row * SimLimits.MaxFighters;
            for (int i = 0; i < SimLimits.MaxFighters; i++)
            {
                ref PositionSample s = ref state.PositionHistory[baseIndex + i];
                if (i < count)
                {
                    ref FighterState f = ref state.Fighters[i];
                    s.Position = f.Position;
                    s.Alive = f.Active && f.Alive;
                }
                else s = default;
            }
            state.PositionHistoryStamp[row] = tick + 1;
        }

        /// <summary>The rewind actually used for an input's <paramref name="viewDelayTicks"/>: 0 in prediction, at most the history and T.</summary>
        public static int EffectiveRewind(MatchState state, int viewDelayTicks)
        {
            if (viewDelayTicks <= 0 || state.Globals.Predicting) return 0;
            int d = viewDelayTicks;
            if (d > SimLimits.MaxRewindTicks) d = SimLimits.MaxRewindTicks;
            int tick = state.Globals.Tick;
            if (d > tick) d = tick;
            return d < 0 ? 0 : d;
        }

        /// <summary>
        /// Position of fighter <paramref name="slot"/> for a hit test rewound by <paramref name="rewindTicks"/> (from
        /// <see cref="EffectiveRewind"/>): its position at the end of tick T − d; the current one when d = 0, when the history does
        /// not hold a tick of the window or when the fighter was not in play at some tick of the window (or is not now).
        /// </summary>
        public static Vec2 TargetPosition(MatchState state, int slot, int rewindTicks)
        {
            ref FighterState f = ref state.Fighters[slot];
            if (rewindTicks <= 0 || !f.Active || !f.Alive) return f.Position;
            int tick = state.Globals.Tick;
            Vec2 position = f.Position;
            for (int k = 1; k <= rewindTicks; k++)
            {
                int t = tick - k;
                if (t < 0) return f.Position;
                int row = t % SimLimits.MaxRewindTicks;
                if (state.PositionHistoryStamp[row] != t + 1) return f.Position;
                ref PositionSample s = ref state.PositionHistory[row * SimLimits.MaxFighters + slot];
                if (!s.Alive) return f.Position;
                position = s.Position;
            }
            return position;
        }
    }
}
