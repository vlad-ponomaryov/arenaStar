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
    ///   - T has an active team reveal (<see cref="MatchState.TeamRevealUntilTick"/>, Nova's ult) — everyone, anywhere;
    ///   - it is not invisible, and either not in a bush or an alive fighter of T is closer than
    ///     <see cref="CombatRules.BushRevealDistance"/>.
    /// Areas (<see cref="AreaState.VisibleToTeams"/>): visible to all, except <see cref="AbilityDef.HiddenFromEnemies"/> ones
    /// (Chomp's mine, D-030) — own team, plus teams with an alive fighter strictly closer than the reveal distance.
    /// In prediction mode only the predicted fighter is recomputed; other fighters and areas are left as shown (Server-Spec §2.5).
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
                if (!state.Globals.Simulates(i)) continue;
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active) continue;
                f.InBush = TileRules.IsBush(grid.Get(GridCell.Of(f.Position)));
            }

            uint revealing = 0u;
            int[] teamReveal = state.TeamRevealUntilTick;
            for (int t = 0; t < teamReveal.Length; t++)
                if (tick < teamReveal[t]) revealing |= TeamBit(t);

            for (int i = 0; i < count; i++)
            {
                if (!state.Globals.Simulates(i)) continue;
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active)
                {
                    f.VisibleToTeams = 0u;
                    continue;
                }

                uint mask = TeamBit(f.Team) | f.MarkedByTeamMask | revealing;
                if (tick < f.RevealedUntilTick) mask = ~0u;
                else if (!f.Invisible)
                {
                    if (!f.InBush) mask = ~0u;
                    else mask |= NearbyTeams(state, i, revealSq);
                }
                f.VisibleToTeams = mask;
            }
            if (state.Globals.Predicting) return; // prediction: masks of other fighters and of areas come from the server (§2.5 rule 5)

            AreaState[] areas = state.Areas;
            for (int i = 0; i < areas.Length; i++)
            {
                ref AreaState a = ref areas[i];
                if (!a.Active)
                {
                    a.VisibleToTeams = 0u;
                    continue;
                }
                AbilityDef ability = defs.GetAbility(a.AbilityRef);
                a.VisibleToTeams = ability != null && ability.HiddenFromEnemies
                    ? TeamBit(a.Team) | TeamsNear(state, a.Position, -1, revealSq)
                    : ~0u;
            }
        }

        /// <summary>Bits of the teams with an alive fighter (other than <paramref name="slot"/>) strictly closer than the reveal distance.</summary>
        private static uint NearbyTeams(MatchState state, int slot, float revealSq) =>
            TeamsNear(state, state.Fighters[slot].Position, slot, revealSq);

        /// <summary>Bits of the teams with an alive fighter (other than <paramref name="except"/>) strictly closer than the reveal distance to <paramref name="p"/>.</summary>
        private static uint TeamsNear(MatchState state, Vec2 p, int except, float revealSq)
        {
            int count = state.Globals.FighterCount;
            uint mask = 0u;
            for (int j = 0; j < count; j++)
            {
                if (j == except) continue;
                ref FighterState o = ref state.Fighters[j];
                if (!o.Active || !o.Alive) continue;
                if (Vec2.SqrDistance(o.Position, p) < revealSq) mask |= TeamBit(o.Team);
            }
            return mask;
        }

        private static uint TeamBit(int team) => (uint)team < 32u ? 1u << team : 0u;
    }
}
