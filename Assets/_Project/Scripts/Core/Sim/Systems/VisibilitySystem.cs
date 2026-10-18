using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Visibility mask <see cref="FighterState.VisibleToTeams"/> of every fighter (D-024), recomputed at the end of each
    /// tick (and at reset), so views, bots and next tick's auto-aim read one consistent answer. A fighter is visible to
    /// team T if T is its own team, or any of:
    ///   - it attacked recently (<see cref="FighterState.RevealedUntilTick"/>, attack reveals for 1 s);
    ///   - it is marked by T (<see cref="FighterState.MarkedByTeamMask"/>);
    ///   - it is not invisible, and either not in a bush or an alive fighter of T is closer than
    ///     <see cref="CombatRules.BushRevealDistance"/>.
    /// The team-wide reveal (Nova's ult) is added with Nova in S2.12.
    /// </summary>
    public static class VisibilitySystem
    {
        public static void Run(MatchState state, MatchDefs defs)
        {
            int count = state.Globals.FighterCount;
            int tick = state.Globals.Tick;
            TileGrid grid = state.Grid;
            float reveal = defs.Rules.BushRevealDistance;
            float revealSq = reveal * reveal;

            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active) continue;
                f.InBush = TileRules.IsBush(grid.Get(GridCell.Of(f.Position)));
            }

            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active)
                {
                    f.VisibleToTeams = 0u;
                    continue;
                }

                uint mask = TeamBit(f.Team) | f.MarkedByTeamMask;
                if (tick < f.RevealedUntilTick) mask = ~0u;
                else if (!f.Invisible)
                {
                    if (!f.InBush) mask = ~0u;
                    else mask |= NearbyTeams(state, i, revealSq);
                }
                f.VisibleToTeams = mask;
            }
        }

        /// <summary>Bits of the teams with an alive fighter (other than <paramref name="slot"/>) strictly closer than the reveal distance.</summary>
        private static uint NearbyTeams(MatchState state, int slot, float revealSq)
        {
            ref FighterState f = ref state.Fighters[slot];
            int count = state.Globals.FighterCount;
            uint mask = 0u;
            for (int j = 0; j < count; j++)
            {
                if (j == slot) continue;
                ref FighterState o = ref state.Fighters[j];
                if (!o.Active || !o.Alive) continue;
                if (Vec2.SqrDistance(o.Position, f.Position) < revealSq) mask |= TeamBit(o.Team);
            }
            return mask;
        }

        private static uint TeamBit(int team) => (uint)team < 32u ? 1u << team : 0u;
    }
}
