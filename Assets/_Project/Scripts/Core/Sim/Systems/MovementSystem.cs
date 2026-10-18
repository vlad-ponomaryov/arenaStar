using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Instant movement without inertia (D-036): velocity = move (length ≤ 1) × fighter speed; the circle of the fighter is
    /// kept inside the arena. Facing follows the aim, otherwise the movement direction.
    /// Fighters do not collide with each other (D-026).
    /// </summary>
    public static class MovementSystem
    {
        public static void Run(MatchState state, MatchDefs defs, PlayerInput[] inputs)
        {
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive)
                {
                    f.Velocity = Vec2.Zero;
                    continue;
                }

                FighterDef def = defs.Fighters[f.DefIndex];
                Vec2 move = inputs[i].Move;
                Vec2 aim = inputs[i].Aim;

                Vec2 old = f.Position;
                if (!move.IsZero)
                {
                    Vec2 desired = old + move * (def.MoveSpeed * SimTime.Dt);
                    f.Position = defs.Bounds.ClampCircle(desired, def.Radius);
                }
                // Actual displacement (stopped by the arena edge) — what views and bots should extrapolate.
                f.Velocity = (f.Position - old) * SimLimits.TickRate;

                if (!aim.IsZero) f.Facing = aim.Normalized;
                else if (!move.IsZero) f.Facing = move.Normalized;
            }
        }
    }
}
