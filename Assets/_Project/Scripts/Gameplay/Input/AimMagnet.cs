using System;
using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Gameplay.Input
{
    /// <summary>
    /// Light aim assist for a manual shot (GDD §5.1, setting "aim magnet"): if a living enemy that the shooter's team sees
    /// (D-024) is within the range and off the manual aim by at most <c>maxDegrees</c>, the aim turns exactly to it (the
    /// closest by angle wins; its length — the fraction of range for lobbed shots — is kept). Applied on the client to the
    /// local input before the tick, so the simulation (and the server later) only sees an ordinary aim. No allocations.
    /// </summary>
    public static class AimMagnet
    {
        /// <param name="aim">Manual aim of <paramref name="slot"/> in simulation space (zero = none).</param>
        /// <param name="range">Range of the ability, cells (enemy centres beyond it are ignored).</param>
        public static Vec2 Apply(MatchState state, int slot, Vec2 aim, float range, float maxDegrees)
        {
            if (aim.IsZero || maxDegrees <= 0f || range <= 0f) return aim;
            ref FighterState f = ref state.Fighters[slot];
            Vec2 dir = aim.Normalized;
            float cosLimit = MathF.Cos(maxDegrees * MathUtil.DegToRad);
            float bestCos = cosLimit;
            Vec2 best = Vec2.Zero;
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (j == slot) continue;
                ref FighterState e = ref state.Fighters[j];
                if (!e.Active || !e.Alive || e.Team == f.Team || !e.IsVisibleTo(f.Team)) continue;
                Vec2 to = e.Position - f.Position;
                float sqr = to.SqrLength;
                if (sqr > range * range || to.IsZero) continue;
                Vec2 toDir = to.Normalized;
                float cos = Vec2.Dot(toDir, dir);
                if (cos < bestCos) continue;
                bestCos = cos;
                best = toDir;
            }
            return best.IsZero ? aim : best * aim.Length;
        }
    }
}
