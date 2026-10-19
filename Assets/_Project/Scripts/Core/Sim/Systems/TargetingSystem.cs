using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Aim resolution in the simulation (D-007): with <see cref="InputButtons.AutoAim"/> the auto-aim target
    /// (<see cref="FindAutoAimTarget"/>, D-129), otherwise the manual aim direction, otherwise the facing.
    /// The auto-aim choice (D-129): candidates are alive enemies visible to the shooter's team (bushes, invisibility —
    /// VisibilitySystem, D-024) with the centre within range, and standing berry boxes with the cell centre within range + ½.
    /// Tiers: 1 — enemy with a clear line, 2 — box with a clear line, 3 — enemy behind a wall (boxes behind walls are never
    /// taken). A clear line matters only for deliveries that walls stop (<c>needsClearLine</c>). In a tier the lowest score wins:
    /// enemies — distance + health weight × health fraction, boxes — distance; ties to the lower index. The last target is kept
    /// for <see cref="CombatRules.AutoAimStickyTicks"/> while it is in the best tier and at most
    /// <see cref="CombatRules.AutoAimStickyMarginCells"/> worse. Without rules — the nearest visible enemy (the S2 behaviour).
    /// </summary>
    public static class TargetingSystem
    {
        private const int TierClearEnemy = 1;
        private const int TierClearBox = 2;
        private const int TierBlockedEnemy = 3;
        private const int TierNone = int.MaxValue;

        /// <summary>Box cells are hit at their edge: half a cell beyond the range still reaches the box.</summary>
        private const float BoxReachSlack = 0.5f;

        /// <summary>Nearest-visible-enemy auto-aim without rules (tests of the S2 behaviour).</summary>
        public static Vec2 ResolveAim(MatchState state, int slot, in PlayerInput input, float range, int rewindTicks = 0) =>
            ResolveAim(state, null, slot, in input, range, false, rewindTicks);

        /// <summary>
        /// Unit direction for an ability of <paramref name="range"/> cells fired by <paramref name="slot"/>; auto-aim looks at the
        /// enemies rewound by <paramref name="rewindTicks"/> (<see cref="LagCompensation"/>; 0 — where they are now) and
        /// remembers the chosen target in the fighter (<c>AimTargetCode</c> / <c>AimTargetTick</c>, stickiness).
        /// </summary>
        public static Vec2 ResolveAim(MatchState state, CombatRules rules, int slot, in PlayerInput input, float range,
            bool needsClearLine, int rewindTicks)
        {
            ref FighterState f = ref state.Fighters[slot];
            if (input.Has(InputButtons.AutoAim))
            {
                int code = FindAutoAimTarget(state, rules, slot, range, needsClearLine, rewindTicks, out Vec2 point);
                if (code != AimTargetCode.None)
                {
                    Vec2 toTarget = (point - f.Position).Normalized;
                    if (!toTarget.IsZero)
                    {
                        f.AimTargetCode = code;
                        f.AimTargetTick = state.Globals.Tick;
                        return toTarget;
                    }
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
                if (!IsEnemyCandidate(in f, in e)) continue;
                float d = Vec2.SqrDistance(LagCompensation.TargetPosition(state, j, rewindTicks), f.Position);
                if (d < best || (found < 0 && d <= best))
                {
                    best = d;
                    found = j;
                }
            }
            return found;
        }

        /// <summary>
        /// Auto-aim target of <paramref name="slot"/> for an ability of <paramref name="range"/> cells (D-129, rules in the class
        /// summary) as an <see cref="AimTargetCode"/>, with the point to aim at; <see cref="AimTargetCode.None"/> — no target.
        /// Read-only: the views call it for the preview, <see cref="ResolveAim"/> for the shot.
        /// </summary>
        public static int FindAutoAimTarget(MatchState state, CombatRules rules, int slot, float range, bool needsClearLine,
            int rewindTicks, out Vec2 point)
        {
            point = Vec2.Zero;
            if (rules == null)
            {
                int nearest = FindNearestEnemy(state, slot, range, rewindTicks);
                if (nearest < 0) return AimTargetCode.None;
                point = LagCompensation.TargetPosition(state, nearest, rewindTicks);
                return AimTargetCode.Fighter(nearest);
            }

            ref FighterState f = ref state.Fighters[slot];
            Vec2 from = f.Position;
            TileGrid grid = state.Grid;
            bool checkFighters = needsClearLine && rules.AutoAimPreferClearShot;
            int lastCode = f.AimTargetCode;
            bool sticky = rules.AutoAimStickyTicks > 0 && lastCode != AimTargetCode.None
                && state.Globals.Tick - f.AimTargetTick <= rules.AutoAimStickyTicks;

            var best = new Candidate { Code = AimTargetCode.None, Tier = TierNone };
            var last = new Candidate { Code = AimTargetCode.None, Tier = TierNone };
            float rangeSq = range * range;

            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (j == slot) continue;
                ref FighterState e = ref state.Fighters[j];
                if (!IsEnemyCandidate(in f, in e)) continue;
                Vec2 p = LagCompensation.TargetPosition(state, j, rewindTicks);
                if (Vec2.SqrDistance(p, from) > rangeSq) continue;
                bool clear = !checkFighters || GridCollision.HasLineOfSight(in grid, from, p);
                float health = e.MaxHealth > 0 ? (float)e.Health / e.MaxHealth : 1f;
                var c = new Candidate
                {
                    Code = AimTargetCode.Fighter(j),
                    Tier = clear ? TierClearEnemy : TierBlockedEnemy,
                    Score = Vec2.Distance(p, from) + rules.AutoAimHealthWeightCells * health,
                    Point = p
                };
                Consider(ref best, ref last, in c, lastCode);
            }

            if (rules.AutoAimTargetsBoxes)
            {
                int width = state.Globals.MapWidth;
                float reach = range + BoxReachSlack;
                BoxState[] boxes = state.Boxes;
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (!boxes[i].Active || width <= 0) continue;
                    int column = boxes[i].Cell % width, row = boxes[i].Cell / width;
                    Vec2 centre = new GridCell(column, row).Center;
                    float d = Vec2.Distance(centre, from);
                    if (d > reach) continue;
                    if (needsClearLine && !ReachesCell(in grid, from, centre, column, row)) continue;
                    var c = new Candidate { Code = AimTargetCode.Box(i), Tier = TierClearBox, Score = d, Point = centre };
                    Consider(ref best, ref last, in c, lastCode);
                }
            }

            if (best.Code == AimTargetCode.None) return AimTargetCode.None;
            Candidate chosen = sticky && last.Code != AimTargetCode.None && last.Tier == best.Tier
                && last.Score <= best.Score + rules.AutoAimStickyMarginCells
                ? last
                : best;
            point = chosen.Point;
            return chosen.Code;
        }

        private struct Candidate
        {
            public int Code;
            public int Tier;
            public float Score;
            public Vec2 Point;
        }

        /// <summary>Lower tier, then strictly lower score (earlier candidates win ties); remembers the last target if seen.</summary>
        private static void Consider(ref Candidate best, ref Candidate last, in Candidate c, int lastCode)
        {
            if (c.Tier < best.Tier || (c.Tier == best.Tier && c.Score < best.Score)) best = c;
            if (c.Code == lastCode) last = c;
        }

        private static bool IsEnemyCandidate(in FighterState f, in FighterState e) =>
            e.Active && e.Alive && e.Team != f.Team && e.IsVisibleTo(f.Team);

        /// <summary>The first projectile-blocking cell on the way to the box centre is the box itself.</summary>
        private static bool ReachesCell(in TileGrid grid, Vec2 from, Vec2 centre, int column, int row)
        {
            Vec2 to = centre - from;
            float length = to.Length;
            if (length <= MathUtil.Epsilon) return true;
            if (!GridCollision.Raycast(in grid, from, to / length, length, out _, out GridCell cell)) return true;
            return cell.Column == column && cell.Row == row;
        }

        private static Vec2 Facing(in FighterState f) => f.Facing.IsZero ? Vec2.Up : f.Facing;
    }
}
