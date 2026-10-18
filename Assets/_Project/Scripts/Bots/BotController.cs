using System;
using Arena.Bots.Defs;
using Arena.Bots.Pathfinding;
using Arena.Bots.Utility;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.State;

namespace Arena.Bots
{
    /// <summary>
    /// All bots of a match (GDD §7, D-022): a bot is a virtual player that writes the same <see cref="PlayerInput"/> as a
    /// human. Call <see cref="Update"/> before every <c>MatchSimulation.Tick</c> with the state after the previous tick;
    /// it writes the inputs of the bot slots only (other slots are left untouched).
    ///
    /// Every tick: perception (which enemies the bot's team sees within the view radius, and since when — reaction delay),
    /// path following, aim and shooting. Every <see cref="BotBehaviourDef.DecisionIntervalTicks"/> (staggered by slot): a
    /// utility decision (<see cref="UtilityScorer"/>: Retreat, Attack, MoveToObjective, Wander), a new goal and an A* path
    /// (re-planned when the goal cell changes or the path is used up).
    /// Shooting: a fresh press (the simulation reacts to the rising edge), no burst in progress, the reaction delay of the
    /// target passed, the target within range with a clear line, the charge rule of <see cref="FireDecision"/>, aim spread
    /// of the difficulty (<see cref="BotAim"/>).
    /// No allocations in <see cref="Update"/>: brains, paths, perception ticks and the pathfinder are allocated here once.
    /// </summary>
    public sealed class BotController
    {
        private const int NotSeen = -1;

        private readonly BotBehaviourDef _behaviour;
        private readonly BotBrain[] _brains = new BotBrain[SimLimits.MaxFighters];
        private readonly BotDifficultyDef[] _difficulty = new BotDifficultyDef[SimLimits.MaxFighters];
        private readonly int[] _seenSince = new int[SimLimits.MaxFighters * SimLimits.MaxFighters];
        private readonly GridCell[] _paths;
        private readonly GridPathfinder _pathfinder;

        public BotController(BotBehaviourDef behaviour)
        {
            if (behaviour == null) throw new ArgumentNullException(nameof(behaviour));
            behaviour.Validate();
            _behaviour = behaviour;
            _paths = new GridCell[SimLimits.MaxFighters * behaviour.MaxPathLength];
            _pathfinder = new GridPathfinder(SimLimits.MaxMapCells, behaviour.PathNodeLimit);
            for (int i = 0; i < _seenSince.Length; i++) _seenSince[i] = NotSeen;
        }

        public BotBehaviourDef Behaviour => _behaviour;

        /// <summary>Number of bot-driven slots.</summary>
        public int BotCount { get; private set; }

        /// <summary>Hands <paramref name="slot"/> to a bot of <paramref name="difficulty"/>; its RNG is derived from the match seed and the slot.</summary>
        public void AddBot(int slot, BotDifficultyDef difficulty, int matchSeed)
        {
            if ((uint)slot >= SimLimits.MaxFighters) throw new ArgumentOutOfRangeException(nameof(slot));
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            difficulty.Validate();
            if (!_brains[slot].Active) BotCount++;
            _difficulty[slot] = difficulty;
            _brains[slot] = new BotBrain
            {
                Active = true,
                Action = BotAction.None,
                TargetSlot = -1,
                Rng = new DeterministicRng(((ulong)(uint)matchSeed << 32) | (uint)(slot + 1)),
                NextDecisionTick = slot % _behaviour.DecisionIntervalTicks, // spread the decisions of the bots over ticks
                StrafeSign = 1
            };
            ClearSeen(slot);
        }

        public bool IsBot(int slot) => (uint)slot < SimLimits.MaxFighters && _brains[slot].Active;

        /// <summary>Copy of a bot's state (tests, debug views).</summary>
        public BotBrain GetBrain(int slot) => _brains[slot];

        public BotDifficultyDef GetDifficulty(int slot) => _difficulty[slot];

        /// <summary>Tick since which <paramref name="bot"/> continuously perceives <paramref name="enemy"/>, or −1.</summary>
        public int FirstSeenTick(int bot, int enemy) => _seenSince[bot * SimLimits.MaxFighters + enemy];

        /// <summary>Writes the inputs of all bot slots for the tick about to be simulated (<c>state.Tick</c>).</summary>
        public void Update(MatchState state, MatchDefs defs, PlayerInput[] outInputs)
        {
            int count = state.Globals.FighterCount;
            int tick = state.Globals.Tick;
            bool ended = state.Mode.IsEnded;
            for (int slot = 0; slot < count; slot++)
            {
                ref BotBrain b = ref _brains[slot];
                if (!b.Active) continue;
                ref FighterState f = ref state.Fighters[slot];
                if (ended || !f.Active)
                {
                    outInputs[slot] = default;
                    continue;
                }
                if (!f.Alive)
                {
                    OnDefeated(ref b, slot, tick);
                    outInputs[slot] = default;
                    continue;
                }

                UpdatePerception(state, slot, tick);
                if (tick >= b.NextDecisionTick)
                {
                    Decide(state, defs, slot, tick);
                    b.NextDecisionTick = tick + _behaviour.DecisionIntervalTicks;
                }
                outInputs[slot] = Act(state, defs, slot, tick);
            }
        }

        private void OnDefeated(ref BotBrain b, int slot, int tick)
        {
            b.Action = BotAction.None;
            b.TargetSlot = -1;
            b.HasPath = false;
            b.NextDecisionTick = tick; // decide right after the respawn
            ClearSeen(slot);
        }

        private void ClearSeen(int slot)
        {
            int start = slot * SimLimits.MaxFighters;
            for (int j = 0; j < SimLimits.MaxFighters; j++) _seenSince[start + j] = NotSeen;
        }

        private void UpdatePerception(MatchState state, int slot, int tick)
        {
            int count = state.Globals.FighterCount;
            int start = slot * SimLimits.MaxFighters;
            for (int j = 0; j < count; j++)
            {
                if (Perception.CanSee(state, slot, j, _behaviour.ViewRadius))
                {
                    if (_seenSince[start + j] == NotSeen) _seenSince[start + j] = tick;
                }
                else _seenSince[start + j] = NotSeen;
            }
        }

        private bool IsPerceived(int slot, int enemy) => enemy >= 0 && _seenSince[slot * SimLimits.MaxFighters + enemy] != NotSeen;

        // ---------------------------------------------------------------- decision

        private void Decide(MatchState state, MatchDefs defs, int slot, int tick)
        {
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];

            int target = Perception.FindNearestEnemy(state, slot, _behaviour.ViewRadius, out float distance);
            bool hasObjective = Perception.FindObjective(state, defs, f.Position, out Vec2 objective);
            var situation = new BotSituation
            {
                HealthFraction = f.MaxHealth > 0 ? (float)f.Health / f.MaxHealth : 0f,
                Retreating = b.Action == BotAction.Retreat,
                HasTarget = target >= 0,
                TargetDistance = distance,
                HasObjective = hasObjective
            };
            BotAction action = UtilityScorer.Choose(in situation, _behaviour);
            BotAction previous = b.Action;
            b.Action = action;
            b.TargetSlot = target; // kept while retreating too: shooting back is allowed

            switch (action)
            {
                case BotAction.Attack:
                    b.Goal = state.Fighters[target].Position;
                    if (tick >= b.StrafeUntilTick)
                    {
                        b.StrafeSign = b.Rng.Range(0, 2) == 0 ? -1 : 1;
                        b.StrafeUntilTick = tick + _behaviour.StrafeSwitchTicks;
                    }
                    break;
                case BotAction.MoveToObjective:
                    b.Goal = objective;
                    break;
                case BotAction.Retreat:
                    b.Goal = NearestOwnSpawn(defs.Map, f.Team, f.Position);
                    break;
                default:
                    if (previous != BotAction.Wander || IsReached(f.Position, b.Goal)) b.Goal = PickWanderGoal(state.Grid, ref b, f.Position);
                    break;
            }
            PlanPath(state, slot, f.Position);
        }

        private void PlanPath(MatchState state, int slot, Vec2 position)
        {
            ref BotBrain b = ref _brains[slot];
            GridCell goalCell = GridCell.Of(b.Goal);
            if (b.HasPath && goalCell == b.PathGoal && b.PathCursor < b.PathLength) return;
            b.PathGoal = goalCell;
            b.PathCursor = 0;
            b.HasPath = _pathfinder.FindPath(state.Grid, GridCell.Of(position), goalCell, _paths, slot * _behaviour.MaxPathLength,
                _behaviour.MaxPathLength, out b.PathLength);
        }

        private static Vec2 NearestOwnSpawn(MapDef map, int team, Vec2 from)
        {
            Vec2 best = map.Center;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < map.Spawns.Length; i++)
            {
                if (map.Spawns[i].Team != team) continue;
                Vec2 c = map.Spawns[i].Cell.Center;
                float d = Vec2.SqrDistance(c, from);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = c;
                }
            }
            return best;
        }

        private Vec2 PickWanderGoal(in TileGrid grid, ref BotBrain b, Vec2 from)
        {
            GridCell c = GridCell.Of(from);
            int r = _behaviour.WanderRadiusCells;
            for (int i = 0; i < _behaviour.WanderAttempts; i++)
            {
                int x = c.Column + b.Rng.Range(-r, r + 1);
                int y = c.Row + b.Rng.Range(-r, r + 1);
                if (!grid.BlocksMovement(x, y)) return new GridCell(x, y).Center;
            }
            return from;
        }

        private bool IsReached(Vec2 position, Vec2 point)
        {
            float reach = _behaviour.WaypointReachDistance;
            return Vec2.SqrDistance(position, point) <= reach * reach;
        }

        // ---------------------------------------------------------------- action (every tick)

        private PlayerInput Act(MatchState state, MatchDefs defs, int slot, int tick)
        {
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];
            FighterDef def = defs.Fighters[f.DefIndex];
            Vec2 position = f.Position;
            int target = IsPerceived(slot, b.TargetSlot) ? b.TargetSlot : -1;

            Vec2 move;
            if (b.Action == BotAction.Attack && target >= 0)
            {
                Vec2 targetPos = state.Fighters[target].Position;
                float distance = Vec2.Distance(position, targetPos);
                bool clear = def.Attack.IgnoresWalls || Perception.HasClearShot(state.Grid, position, targetPos);
                if (distance > def.AttackRange * _behaviour.EngageRangeFraction || !clear)
                    move = FollowPath(slot, position, targetPos);
                else
                {
                    Vec2 to = (targetPos - position).Normalized;
                    move = new Vec2(-to.Y, to.X) * b.StrafeSign;
                }
            }
            else if (b.Action == BotAction.None) move = Vec2.Zero;
            else move = FollowPath(slot, position, b.Goal); // an attack target out of sight: its last known position

            Vec2 aim = Vec2.Zero;
            InputButtons buttons = InputButtons.None;
            if (target >= 0 && TryAim(state, defs, slot, target, tick, out aim)) buttons = InputButtons.Attack;
            return new PlayerInput(move, aim, buttons);
        }

        /// <summary>Direction to the next waypoint (the exact goal point on the last one), or straight to the goal without a path.</summary>
        private Vec2 FollowPath(int slot, Vec2 position, Vec2 goal)
        {
            ref BotBrain b = ref _brains[slot];
            if (b.HasPath)
            {
                int start = slot * _behaviour.MaxPathLength;
                while (b.PathCursor < b.PathLength && IsReached(position, _paths[start + b.PathCursor].Center)) b.PathCursor++;
                if (b.PathCursor < b.PathLength)
                {
                    GridCell cell = _paths[start + b.PathCursor];
                    bool last = b.PathCursor == b.PathLength - 1 && cell == GridCell.Of(goal);
                    return ((last ? goal : cell.Center) - position).Normalized;
                }
            }
            return IsReached(position, goal) ? Vec2.Zero : (goal - position).Normalized;
        }

        /// <summary>True (and the spread aim direction) if the bot presses the attack this tick.</summary>
        private bool TryAim(MatchState state, MatchDefs defs, int slot, int target, int tick, out Vec2 aim)
        {
            aim = Vec2.Zero;
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];
            if (f.BurstShotsLeft > 0 || f.Charges <= 0 || f.IsHeld(InputButtons.Attack) || f.IsDashing || f.Stunned) return false;
            if (tick < b.NextShotTick) return false;
            BotDifficultyDef difficulty = _difficulty[slot];
            if (tick < FirstSeenTick(slot, target) + difficulty.ReactionTicks) return false;

            FighterDef def = defs.Fighters[f.DefIndex];
            ref FighterState t = ref state.Fighters[target];
            float distance = Vec2.Distance(f.Position, t.Position);
            if (distance > def.AttackRange * _behaviour.FireRangeFraction) return false;
            if (!def.Attack.IgnoresWalls && !Perception.HasClearShot(state.Grid, f.Position, t.Position)) return false;
            int damage = FireDecision.EstimateAttackDamage(def, f.Level, defs.Rules);
            if (!FireDecision.ShouldSpendCharge(f.Charges, t.Health, damage, distance, _behaviour.LastChargeEnemyDistance)) return false;

            aim = BotAim.Apply(t.Position - f.Position, difficulty.AimSpreadDegrees, ref b.Rng);
            if (aim.IsZero) return false;
            b.NextShotTick = tick + _behaviour.MinShotIntervalTicks;
            return true;
        }
    }
}
