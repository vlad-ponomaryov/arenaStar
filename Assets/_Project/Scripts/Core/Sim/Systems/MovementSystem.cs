using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Instant movement without inertia (D-036): velocity = move (length ≤ 1) × fighter speed × slow (<c>SpeedMul</c>) ×
    /// talent; the body circle collides with movement-blocking tiles (walls, water) and slides along them
    /// (<see cref="GridCollision.MoveCircle"/>; the map edge is a wall). Facing follows the aim, otherwise the movement direction.
    /// Priority: a knockback in progress moves the fighter (even when stunned) and ignores its input; a stunned fighter
    /// stands; a dashing fighter is moved by <see cref="DashSystem"/> instead. Fighters do not collide with each other (D-026).
    /// </summary>
    public static class MovementSystem
    {
        public static void Run(MatchState state, MatchDefs defs, PlayerInput[] inputs)
        {
            int count = state.Globals.FighterCount;
            TileGrid grid = state.Grid;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive)
                {
                    f.Velocity = Vec2.Zero;
                    continue;
                }

                FighterDef def = defs.Fighters[f.DefIndex];
                Vec2 old = f.Position;

                if (f.KnockbackTicks > 0)
                {
                    f.Position = GridCollision.MoveCircle(in grid, old, f.KnockbackVelocity * SimTime.Dt, def.Radius);
                    f.Velocity = (f.Position - old) * SimLimits.TickRate;
                    if (--f.KnockbackTicks == 0) f.KnockbackVelocity = Vec2.Zero;
                    continue;
                }
                if (f.Stunned || f.IsDashing)
                {
                    f.Velocity = Vec2.Zero;
                    continue;
                }

                Vec2 move = inputs[i].Move;
                Vec2 aim = inputs[i].Aim;
                if (!move.IsZero)
                {
                    float speed = def.MoveSpeed * f.SpeedMul * TalentSystem.MoveSpeedMultiplier(state, defs, i);
                    f.Position = GridCollision.MoveCircle(in grid, old, move * (speed * SimTime.Dt), def.Radius);
                }
                // Actual displacement (stopped / deflected by walls) — what views and bots should extrapolate.
                f.Velocity = (f.Position - old) * SimLimits.TickRate;

                if (!aim.IsZero) f.Facing = aim.Normalized;
                else if (!move.IsZero) f.Facing = move.Normalized;
            }
        }
    }
}
