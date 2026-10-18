using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Aim resolution in the simulation (D-007): with <see cref="InputButtons.AutoAim"/> the nearest enemy within range
    /// visible to the shooter's team (bushes, invisibility: VisibilitySystem, D-024; centre distance; ties to the
    /// lower slot), otherwise the manual aim direction, otherwise the facing.
    /// </summary>
    public static class TargetingSystem
    {
        /// <summary>
        /// Unit direction for an ability of <paramref name="range"/> cells fired by <paramref name="slot"/>; auto-aim looks at the
        /// enemies rewound by <paramref name="rewindTicks"/> (<see cref="LagCompensation"/>; 0 — where they are now).
        /// </summary>
        public static Vec2 ResolveAim(MatchState state, int slot, in PlayerInput input, float range, int rewindTicks = 0)
        {
            ref FighterState f = ref state.Fighters[slot];
            if (input.Has(InputButtons.AutoAim))
            {
                int target = FindNearestEnemy(state, slot, range, rewindTicks);
                if (target >= 0)
                {
                    Vec2 toTarget = (LagCompensation.TargetPosition(state, target, rewindTicks) - f.Position).Normalized;
                    if (!toTarget.IsZero) return toTarget;
                }
                return Facing(in f);
            }
            if (!input.Aim.IsZero) return input.Aim.Normalized;
            return Facing(in f);
        }

        /// <summary>
        /// Slot of the nearest alive enemy visible to the shooter's team whose centre is within <paramref name="range"/>, or −1;
        /// distances to the enemies rewound by <paramref name="rewindTicks"/> (visibility — as now).
        /// </summary>
        public static int FindNearestEnemy(MatchState state, int slot, float range, int rewindTicks = 0)
        {
            ref FighterState f = ref state.Fighters[slot];
            int count = state.Globals.FighterCount;
            float best = range * range;
            int found = -1;
            for (int j = 0; j < count; j++)
            {
                if (j == slot) continue;
                ref FighterState e = ref state.Fighters[j];
                if (!e.Active || !e.Alive || e.Team == f.Team || !e.IsVisibleTo(f.Team)) continue;
                float d = Vec2.SqrDistance(LagCompensation.TargetPosition(state, j, rewindTicks), f.Position);
                if (d < best || (found < 0 && d <= best))
                {
                    best = d;
                    found = j;
                }
            }
            return found;
        }

        private static Vec2 Facing(in FighterState f) => f.Facing.IsZero ? Vec2.Up : f.Facing;
    }
}
