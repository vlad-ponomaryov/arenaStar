using System;
using Arena.Bots.Defs;
using Arena.Bots.Memory;
using Arena.Bots.Pathfinding;
using Arena.Bots.Utility;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Random;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Bots
{
    /// <summary>
    /// All bots of a match (GDD §7, D-022, D-057, D-058): a bot is a virtual player that writes the same
    /// <see cref="PlayerInput"/> as a human. Call <see cref="Update(MatchState, MatchDefs, SimEventBuffer, PlayerInput[])"/>
    /// before every <c>MatchSimulation.Tick</c> with the state and the events of the previous tick; it writes the inputs of
    /// the bot slots only (other slots are left untouched).
    ///
    /// Every tick: who damaged the bots (events → <see cref="EnemyMemory"/>), perception (enemies the bot's team sees within
    /// the view radius, since when — reaction delay — and where they were last seen), path following, then one press:
    /// a pending ult, else a pending trick, else an attack (at the target, at a remembered bush, or a mistaken shot at an
    /// empty bush). Every <see cref="BotBehaviourDef.DecisionIntervalTicks"/> (staggered by slot): a utility decision
    /// (<see cref="UtilityScorer"/>), a new goal and an A* path, and the ult / trick / mistake rolls
    /// (<see cref="UltDecision"/>, <see cref="TrickDecision"/>, <see cref="BotDifficultyDef"/>).
    /// Shooting: a fresh press (the simulation reacts to the rising edge), no burst in progress, the reaction delay of the
    /// target passed, the target within range with a clear line, the charge rule of <see cref="FireDecision"/>, aim spread
    /// of the difficulty (<see cref="BotAim"/>). Shots at bushes never spend the last charge.
    /// Last Beast pace (S3.14, D-105): without an enemy or a berry the bot breaks the nearest berry box that is not in the fog
    /// soon (<see cref="BotAction.BreakBox"/>: from a free cell next to it, never with the last charge), otherwise explores
    /// safe cells within <c>ExploreRadiusCells</c>; fleeing the fog leads to the nearest point inside the next safe square
    /// (not everybody to the centre). Boxes are read from the tiles, so a client bot on a display state sees them too.
    /// No allocations in Update: brains, paths, memory and the pathfinder are allocated here once.
    /// </summary>
    public sealed class BotController
    {
        private const int NotSeen = -1;

        /// <summary>Half a cell plus a little: from a box centre to just outside its near face (geometry, not tuning).</summary>
        private const float BoxFaceOffset = 0.55f;

        private readonly BotBehaviourDef _behaviour;
        private readonly BotBrain[] _brains = new BotBrain[SimLimits.MaxFighters];
        private readonly BotDifficultyDef[] _difficulty = new BotDifficultyDef[SimLimits.MaxFighters];
        private readonly int[] _seenSince = new int[SimLimits.MaxFighters * SimLimits.MaxFighters];
        private readonly EnemyMemory _memory = new EnemyMemory();
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

        /// <summary>What the bots remember about the enemies (tests, debug views).</summary>
        public EnemyMemory Memory => _memory;

        /// <summary>Number of bot-driven slots.</summary>
        public int BotCount { get; private set; }

        /// <summary>Hands <paramref name="slot"/> to a bot of <paramref name="difficulty"/>; its RNG is derived from the match seed and the slot.</summary>
        public void AddBot(int slot, BotDifficultyDef difficulty, int matchSeed) => SetBotActive(slot, true, difficulty, matchSeed, 0);

        /// <summary>
        /// Takes over a slot or hands it back (online: disconnect, AFK, leave — Server-Spec §7). On: a brain from scratch
        /// (no memory, nothing seen), its RNG from the match seed, the slot and <paramref name="takeoverIndex"/> (0 — the same
        /// bot as <see cref="AddBot"/>; the n-th takeover of a slot — another stream). Off: the slot's input is no longer
        /// written and its memory is dropped. No allocations.
        /// </summary>
        public void SetBotActive(int slot, bool active, BotDifficultyDef difficulty, int matchSeed, int takeoverIndex)
        {
            if ((uint)slot >= SimLimits.MaxFighters) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!active)
            {
                if (!_brains[slot].Active) return;
                BotCount--;
                _brains[slot] = default;
                ClearSeen(slot);
                _memory.Clear(slot);
                return;
            }
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            if (takeoverIndex < 0) throw new ArgumentOutOfRangeException(nameof(takeoverIndex));
            difficulty.Validate();
            if (!_brains[slot].Active) BotCount++;
            _difficulty[slot] = difficulty;
            uint stream = (uint)(slot + 1 + takeoverIndex * SimLimits.MaxFighters);
            _brains[slot] = new BotBrain
            {
                Active = true,
                Action = BotAction.None,
                TargetSlot = -1,
                MemoryEnemy = -1,
                Rng = new DeterministicRng(((ulong)(uint)matchSeed << 32) | stream),
                NextDecisionTick = slot % _behaviour.DecisionIntervalTicks, // spread the decisions of the bots over ticks
                StrafeSign = 1
            };
            RollRetreatShift(ref _brains[slot], difficulty);
            ClearSeen(slot);
            _memory.Clear(slot);
        }

        public bool IsBot(int slot) => (uint)slot < SimLimits.MaxFighters && _brains[slot].Active;

        /// <summary>Copy of a bot's state (tests, debug views).</summary>
        public BotBrain GetBrain(int slot) => _brains[slot];

        public BotDifficultyDef GetDifficulty(int slot) => _difficulty[slot];

        /// <summary>Tick since which <paramref name="bot"/> continuously perceives <paramref name="enemy"/>, or −1.</summary>
        public int FirstSeenTick(int bot, int enemy) => _seenSince[bot * SimLimits.MaxFighters + enemy];

        /// <summary>Same as the full overload without the events of the last tick (bots then do not know who damaged them).</summary>
        public void Update(MatchState state, MatchDefs defs, PlayerInput[] outInputs) => Update(state, defs, null, outInputs);

        /// <summary>
        /// Writes the inputs of all bot slots for the tick about to be simulated (<c>state.Tick</c>).
        /// <paramref name="lastTickEvents"/> — the simulation's events of the previous tick (may be null).
        /// </summary>
        public void Update(MatchState state, MatchDefs defs, SimEventBuffer lastTickEvents, PlayerInput[] outInputs)
        {
            if (lastTickEvents != null) ObserveDamage(state, lastTickEvents);
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

        private void ObserveDamage(MatchState state, SimEventBuffer events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                ref readonly SimEvent e = ref events[i];
                if (e.Type != SimEventType.Damaged) continue;
                if ((uint)e.Target >= SimLimits.MaxFighters || (uint)e.Source >= SimLimits.MaxFighters || e.Source == e.Target) continue;
                if (!_brains[e.Target].Active || state.Fighters[e.Source].Team == state.Fighters[e.Target].Team) continue;
                _memory.Damaged(e.Target, e.Source, e.Tick);
            }
        }

        private void OnDefeated(ref BotBrain b, int slot, int tick)
        {
            b.Action = BotAction.None;
            b.TargetSlot = -1;
            b.MemoryEnemy = -1;
            b.HasPath = false;
            b.UltPending = false;
            b.TrickPending = false;
            b.BushShotPending = false;
            b.Dodging = false;
            b.NextDecisionTick = tick; // decide right after the respawn
            RollRetreatShift(ref b, _difficulty[slot]);
            ClearSeen(slot);
            _memory.Clear(slot);
        }

        private static void RollRetreatShift(ref BotBrain b, BotDifficultyDef difficulty)
        {
            float jitter = difficulty.RetreatThresholdJitter;
            b.RetreatShift = jitter > 0f ? b.Rng.Range(-jitter, jitter) : 0f;
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
                    ref FighterState e = ref state.Fighters[j];
                    _memory.Seen(slot, j, tick, e.Position, e.Velocity);
                }
                else _seenSince[start + j] = NotSeen;
            }
        }

        private bool IsPerceived(int slot, int enemy) => enemy >= 0 && _seenSince[slot * SimLimits.MaxFighters + enemy] != NotSeen;

        private uint PerceivedMask(int slot, int count)
        {
            uint mask = 0;
            for (int j = 0; j < count; j++)
                if (IsPerceived(slot, j)) mask |= 1u << j;
            return mask;
        }

        // ---------------------------------------------------------------- decision

        private void Decide(MatchState state, MatchDefs defs, int slot, int tick)
        {
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];
            FighterDef def = defs.Fighters[f.DefIndex];
            BotDifficultyDef difficulty = _difficulty[slot];
            b.UltPending = false;
            b.TrickPending = false;
            b.BushShotPending = false;

            float distance;
            int target = difficulty.FocusHealthWeightCells > 0f
                ? Perception.FindFocusTarget(state, slot, _behaviour.ViewRadius, difficulty.FocusHealthWeightCells, out distance)
                : Perception.FindNearestEnemy(state, slot, _behaviour.ViewRadius, out distance);
            int avoided = -1;
            if (target >= 0 && IsCautious(state, defs) && distance > _behaviour.CautiousEngageDistance && !Provoked(slot, target, tick))
            {
                avoided = target; // Last Beast: leave a far enemy alone early on and keep away from it
                target = -1;
            }
            bool hasObjective = Perception.FindObjective(state, defs, f.Position, _behaviour.LootRadiusCells, out Vec2 objective);
            float fireRange = def.AttackRange * _behaviour.FireRangeFraction;
            int memoryEnemy = FindBushMemory(state, defs, slot, tick, out Vec2 bushPoint);
            var situation = new BotSituation
            {
                HealthFraction = f.MaxHealth > 0 ? (float)f.Health / f.MaxHealth : 0f,
                Retreating = b.Action == BotAction.Retreat || b.Action == BotAction.HideInBush,
                HasTarget = target >= 0,
                TargetDistance = distance,
                HasObjective = hasObjective,
                TargetInFireRange = target >= 0 && distance <= fireRange,
                RetreatShift = b.RetreatShift,
                IsCarrier = defs.Mode == ModeKind.Acorns && f.CarriedAcorns >= _behaviour.CarrierAcorns,
                HasBushMemory = memoryEnemy >= 0,
                FogThreat = LastBeastRules.IsInFogSoon(state, defs, f.Position, _behaviour.FogWarnTicks)
            };
            GridCell box = default, boxStand = default;
            if (defs.Mode == ModeKind.LastBeast && target < 0)
                situation.HasBox = FindBox(state, defs, f.Position, out box, out boxStand);
            Vec2 hideBush = Vec2.Zero;
            bool wantsRetreat = UtilityScorer.WantsRetreat(in situation, _behaviour);
            if (wantsRetreat)
            {
                Vec2 threat = target >= 0 ? state.Fighters[target].Position : f.Position;
                situation.HasHideBush = FindHideBush(state, defs, f.Position, threat, target >= 0, out hideBush);
            }

            BotAction action = UtilityScorer.Choose(in situation, _behaviour);
            BotAction previous = b.Action;
            b.Action = action;
            b.TargetSlot = target; // kept while retreating too: shooting back is allowed
            b.MemoryEnemy = -1;

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
                    b.Goal = defs.Mode == ModeKind.LastBeast
                        ? AwayGoal(state, defs, f.Position, target >= 0 ? state.Fighters[target].Position : f.Position)
                        : NearestOwnSpawn(defs.Map, f.Team, f.Position);
                    break;
                case BotAction.HideInBush:
                    b.Goal = hideBush;
                    break;
                case BotAction.CheckBush:
                    b.Goal = bushPoint;
                    b.MemoryEnemy = memoryEnemy;
                    break;
                case BotAction.StayWithTeam:
                    b.Goal = TeamSpot(state, defs, slot);
                    break;
                case BotAction.FleeFog:
                    b.Goal = FogGoal(state, defs, f.Position);
                    break;
                case BotAction.BreakBox:
                    b.BoxCell = box;
                    b.Goal = boxStand.Center;
                    break;
                default:
                    if (avoided >= 0) b.Goal = AwayGoal(state, defs, f.Position, state.Fighters[avoided].Position);
                    else if (previous != BotAction.Wander || IsReached(f.Position, b.Goal))
                        b.Goal = defs.Mode == ModeKind.LastBeast && _behaviour.ExploreRadiusCells > 0
                            ? PickExploreGoal(state, defs, ref b, f.Position)
                            : PickWanderGoal(state.Grid, ref b, f.Position);
                    break;
            }
            PlanPath(state, slot, f.Position);

            if (target >= 0) DecideUlt(state, defs, slot, tick, target, distance);
            if (!b.UltPending) DecideTrick(state, defs, slot, tick, target, distance, wantsRetreat, hasObjective, objective);
            if (target < 0 && !wantsRetreat && action != BotAction.CheckBush && difficulty.EmptyBushShotChance > 0f
                && b.Rng.NextFloat01() < difficulty.EmptyBushShotChance)
                b.BushShotPending = FindRandomBush(state.Grid, f.Position, fireRange, ref b.Rng, out b.BushShotPoint);
            // Last roll of the decision: difficulties without dodging draw nothing (their matches stay as they were).
            b.Dodging = difficulty.DodgeChance > 0f && b.Rng.NextFloat01() < difficulty.DodgeChance;
        }

        private void DecideUlt(MatchState state, MatchDefs defs, int slot, int tick, int target, float distance)
        {
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];
            FighterDef def = defs.Fighters[f.DefIndex];
            AbilityDef ult = def.Ult;
            if (ult == null || !UltSystem.IsCharged(defs, in f)) return;
            BotDifficultyDef difficulty = _difficulty[slot];
            if (tick < FirstSeenTick(slot, target) + difficulty.ReactionTicks) return;

            ref FighterState t = ref state.Fighters[target];
            Vec2 dir = (t.Position - f.Position).Normalized;
            if (dir.IsZero) return;
            if (NeedsClearShot(ult) && !Perception.HasClearShot(state.Grid, f.Position, t.Position)) return;
            float targetRadius = defs.Fighters[t.DefIndex].Radius;
            bool inZone = ult.Delivery == DeliveryKind.SelfBuff
                ? distance <= def.AttackRange * _behaviour.FireRangeFraction
                : UltDecision.IsInZone(ult, f.Position, dir, def.Radius, t.Position, targetRadius);
            bool worth = false;
            if (inZone && UltDecision.IsEngageUlt(ult)) worth = true;
            else if (inZone)
            {
                int enemies = UltDecision.CountInZone(state, defs, slot, ult, dir, PerceivedMask(slot, state.Globals.FighterCount));
                int damage = UltDecision.EstimateDamageOnTarget(ult, f.Level, defs.Rules, distance, targetRadius);
                worth = UltDecision.IsWorthIt(enemies, t.Health, damage, _behaviour);
            }
            b.UltPending = UltDecision.Roll(worth, inZone, difficulty, ref b.Rng);
        }

        private void DecideTrick(MatchState state, MatchDefs defs, int slot, int tick, int target, float distance, bool wantsRetreat,
            bool hasObjective, Vec2 objective)
        {
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];
            FighterDef def = defs.Fighters[f.DefIndex];
            AbilityDef trick = def.Trick;
            if (trick == null || def.BotTrickHint == BotTrickHint.None || !TrickSystem.IsReady(defs, in f, tick)) return;

            float reach = trick.Delivery == DeliveryKind.Dash ? trick.DashDistance : trick.Range;
            var s = new TrickSituation
            {
                RecentAttackers = _memory.CountRecentAttackers(slot, tick, _behaviour.FocusWindowTicks),
                Retreating = wantsRetreat,
                HasTarget = target >= 0,
                TargetDistance = distance,
                TargetInEngageReach = target >= 0 && distance <= reach + def.AttackRange,
                LowestAllyHealthFraction = LowestAllyHealth(state, slot, trick.Range),
                ObjectiveDistance = hasObjective ? Vec2.Distance(f.Position, objective) : float.PositiveInfinity
            };
            if (!TrickDecision.Roll(def.BotTrickHint, in s, _behaviour, _difficulty[slot], ref b.Rng)) return;

            Vec2 dir = Vec2.Zero;
            if (target >= 0)
            {
                Vec2 to = state.Fighters[target].Position - f.Position;
                dir = def.BotTrickHint == BotTrickHint.Escape ? -to : to;
            }
            else if (wantsRetreat) dir = b.Goal - f.Position;
            dir = dir.Normalized;
            if (dir.IsZero) dir = f.Facing.IsZero ? Vec2.Up : f.Facing;
            b.TrickPending = true;
            b.TrickDir = dir;
        }

        private static float LowestAllyHealth(MatchState state, int slot, float range)
        {
            ref FighterState f = ref state.Fighters[slot];
            float lowest = f.MaxHealth > 0 ? (float)f.Health / f.MaxHealth : 1f;
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (j == slot) continue;
                ref FighterState a = ref state.Fighters[j];
                if (!a.Active || !a.Alive || a.Team != f.Team || a.MaxHealth <= 0) continue;
                if (Vec2.SqrDistance(a.Position, f.Position) > range * range) continue;
                float h = (float)a.Health / a.MaxHealth;
                if (h < lowest) lowest = h;
            }
            return lowest;
        }

        /// <summary>
        /// The most recently lost enemy (within <c>MemoryTicks</c>, still alive) whose extrapolated position has a bush within
        /// <c>BushNearCells</c> outside the coming fog; <paramref name="point"/> = the centre of that bush cell. −1 if none.
        /// </summary>
        private int FindBushMemory(MatchState state, MatchDefs defs, int slot, int tick, out Vec2 point)
        {
            point = Vec2.Zero;
            int best = -1;
            int bestTick = EnemyMemory.Never;
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (IsPerceived(slot, j)) continue;
                int seen = _memory.LastSeenTick(slot, j);
                if (seen == EnemyMemory.Never) continue;
                if (tick - seen > _behaviour.MemoryTicks || !state.Fighters[j].Alive)
                {
                    _memory.ForgetSighting(slot, j);
                    continue;
                }
                if (seen <= bestTick) continue;
                Vec2 predicted = _memory.PredictedPosition(slot, j, tick, _behaviour.MemoryPredictTicks);
                if (!FindNearestBush(state, defs, predicted, _behaviour.BushNearCells, out Vec2 bush)) continue;
                best = j;
                bestTick = seen;
                point = bush;
            }
            return best;
        }

        /// <summary>Nearest bush cell within <paramref name="radiusCells"/> that the fog does not cover within <c>FogWarnTicks</c>.</summary>
        private bool FindNearestBush(MatchState state, MatchDefs defs, Vec2 p, int radiusCells, out Vec2 point)
        {
            point = Vec2.Zero;
            TileGrid grid = state.Grid;
            GridCell c = GridCell.Of(p);
            float bestSq = float.PositiveInfinity;
            for (int dy = -radiusCells; dy <= radiusCells; dy++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                if (!TileRules.IsBush(grid.Get(c.Column + dx, c.Row + dy))) continue;
                Vec2 center = new GridCell(c.Column + dx, c.Row + dy).Center;
                float d = Vec2.SqrDistance(center, p);
                if (d < bestSq && !LastBeastRules.IsInFogSoon(state, defs, center, _behaviour.FogWarnTicks))
                {
                    bestSq = d;
                    point = center;
                }
            }
            return bestSq < float.PositiveInfinity;
        }

        /// <summary>
        /// Nearest bush cell within <c>HideBushRadiusCells</c> that is not nearer to the threat than the bot is now and that the fog
        /// does not cover within <c>FogWarnTicks</c> (else the bot would swing between hiding and fleeing the fog).
        /// </summary>
        private bool FindHideBush(MatchState state, MatchDefs defs, Vec2 position, Vec2 threat, bool hasThreat, out Vec2 point)
        {
            point = Vec2.Zero;
            TileGrid grid = state.Grid;
            int r = _behaviour.HideBushRadiusCells;
            if (r <= 0) return false;
            GridCell c = GridCell.Of(position);
            float maxSq = (r + 0.5f) * (r + 0.5f);
            float threatSq = Vec2.SqrDistance(position, threat);
            float bestSq = float.PositiveInfinity;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (!TileRules.IsBush(grid.Get(c.Column + dx, c.Row + dy))) continue;
                Vec2 center = new GridCell(c.Column + dx, c.Row + dy).Center;
                float d = Vec2.SqrDistance(center, position);
                if (d > maxSq || d >= bestSq) continue;
                if (hasThreat && Vec2.SqrDistance(center, threat) < threatSq) continue;
                if (LastBeastRules.IsInFogSoon(state, defs, center, _behaviour.FogWarnTicks)) continue;
                bestSq = d;
                point = center;
            }
            return bestSq < float.PositiveInfinity;
        }

        /// <summary>A random bush cell within <paramref name="range"/> on a clear line (a mistaken shot), from a few attempts.</summary>
        private bool FindRandomBush(in TileGrid grid, Vec2 position, float range, ref DeterministicRng rng, out Vec2 point)
        {
            point = Vec2.Zero;
            GridCell c = GridCell.Of(position);
            int r = (int)range;
            if (r < 1) return false;
            for (int i = 0; i < _behaviour.EmptyBushShotAttempts; i++)
            {
                int x = c.Column + rng.Range(-r, r + 1);
                int y = c.Row + rng.Range(-r, r + 1);
                if (!TileRules.IsBush(grid.Get(x, y))) continue;
                Vec2 center = new GridCell(x, y).Center;
                float d = Vec2.Distance(center, position);
                if (d > range || d < 1f || !Perception.HasClearShot(grid, position, center)) continue;
                point = center;
                return true;
            }
            return false;
        }

        /// <summary>Carrier spot: the centre of the alive allies moved <c>CarrierBehindDistance</c> towards the own spawn.</summary>
        private Vec2 TeamSpot(MatchState state, MatchDefs defs, int slot)
        {
            ref FighterState f = ref state.Fighters[slot];
            Vec2 sum = Vec2.Zero;
            int n = 0;
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (j == slot) continue;
                ref FighterState a = ref state.Fighters[j];
                if (!a.Active || !a.Alive || a.Team != f.Team) continue;
                sum = sum + a.Position;
                n++;
            }
            if (n == 0) return NearestOwnSpawn(defs.Map, f.Team, f.Position);
            Vec2 centre = sum / n;
            Vec2 back = NearestOwnSpawn(defs.Map, f.Team, centre) - centre;
            float length = back.Length;
            float behind = _behaviour.CarrierBehindDistance;
            Vec2 spot = length > behind ? centre + back * (behind / length) : centre + back;
            GridCell cell = GridCell.Of(spot);
            return state.Grid.BlocksMovement(cell.Column, cell.Row) ? centre : spot;
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

        /// <summary>
        /// Nearest berry box tile within <c>LootRadiusCells</c> whose centre is not in the fog soon and that has a free
        /// side cell; <paramref name="stand"/> = that side cell nearest to the bot.
        /// </summary>
        private bool FindBox(MatchState state, MatchDefs defs, Vec2 from, out GridCell box, out GridCell stand)
        {
            box = default;
            stand = default;
            int r = _behaviour.LootRadiusCells;
            if (r <= 0) return false;
            GridCell c = GridCell.Of(from);
            float bestSq = float.PositiveInfinity;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = c.Column + dx, y = c.Row + dy;
                if (state.Grid.Get(x, y) != TileType.BerryBox) continue;
                var cell = new GridCell(x, y);
                float d = Vec2.SqrDistance(cell.Center, from);
                if (d >= bestSq) continue;
                if (LastBeastRules.IsInFogSoon(state, defs, cell.Center, _behaviour.FogWarnTicks)) continue;
                if (!FindFreeSide(state.Grid, cell, from, out GridCell side)) continue;
                bestSq = d;
                box = cell;
                stand = side;
            }
            return bestSq < float.PositiveInfinity;
        }

        private static bool FindFreeSide(in TileGrid grid, GridCell cell, Vec2 from, out GridCell side)
        {
            side = default;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                int x = cell.Column + (i == 0 ? 1 : i == 1 ? -1 : 0);
                int y = cell.Row + (i == 2 ? 1 : i == 3 ? -1 : 0);
                if (grid.BlocksMovement(x, y)) continue;
                var n = new GridCell(x, y);
                float d = Vec2.SqrDistance(n.Center, from);
                if (d >= bestSq) continue;
                bestSq = d;
                side = n;
            }
            return bestSq < float.PositiveInfinity;
        }

        private bool IsCautious(MatchState state, MatchDefs defs) =>
            defs.Mode == ModeKind.LastBeast && _behaviour.CautiousEngageDistance > 0f
            && LastBeastRules.CountAlive(state) > _behaviour.CautiousUntilAlive;

        private bool Provoked(int slot, int enemy, int tick)
        {
            int hit = _memory.LastDamagedTick(slot, enemy);
            return hit != EnemyMemory.Never && tick - hit <= _behaviour.ProvokedTicks;
        }

        /// <summary>Last Beast retreat (no own spawn): <c>ExploreRadiusCells</c> away from the threat, kept inside the next safe
        /// square (<see cref="FogGoal"/>).</summary>
        private Vec2 AwayGoal(MatchState state, MatchDefs defs, Vec2 from, Vec2 threat)
        {
            Vec2 away = (from - threat).Normalized;
            if (away.IsZero) away = (LastBeastRules.SafeCenter(state) - from).Normalized;
            return FogGoal(state, defs, from + away * _behaviour.ExploreRadiusCells);
        }

        /// <summary>The own position clamped into the next safe square shrunk by <c>FogSafeMarginCells</c> (its centre if
        /// smaller), moved to a walkable cell nearby (towards the centre).</summary>
        private Vec2 FogGoal(MatchState state, MatchDefs defs, Vec2 from)
        {
            LastBeastRules.NextSafeRect(state, defs, out Vec2 min, out Vec2 max);
            float m = _behaviour.FogSafeMarginCells;
            float x = max.X - min.X > 2f * m ? MathUtil.Clamp(from.X, min.X + m, max.X - m) : (min.X + max.X) * 0.5f;
            float y = max.Y - min.Y > 2f * m ? MathUtil.Clamp(from.Y, min.Y + m, max.Y - m) : (min.Y + max.Y) * 0.5f;
            var goal = new Vec2(x, y);
            GridCell g = GridCell.Of(goal);
            if (!state.Grid.BlocksMovement(g.Column, g.Row)) return goal;
            Vec2 centre = LastBeastRules.SafeCenter(state);
            for (int ring = 1; ring <= _behaviour.WanderRadiusCells; ring++)
            {
                float bestSq = float.PositiveInfinity;
                Vec2 best = goal;
                for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (dx != -ring && dx != ring && dy != -ring && dy != ring) continue;
                    if (state.Grid.BlocksMovement(g.Column + dx, g.Row + dy)) continue;
                    Vec2 p = new GridCell(g.Column + dx, g.Row + dy).Center;
                    float d = Vec2.SqrDistance(p, centre);
                    if (d < bestSq)
                    {
                        bestSq = d;
                        best = p;
                    }
                }
                if (bestSq < float.PositiveInfinity) return best;
            }
            return centre;
        }

        /// <summary>Last Beast wandering: a random walkable cell within <c>ExploreRadiusCells</c> that the fog does not cover soon.</summary>
        private Vec2 PickExploreGoal(MatchState state, MatchDefs defs, ref BotBrain b, Vec2 from)
        {
            GridCell c = GridCell.Of(from);
            int r = _behaviour.ExploreRadiusCells;
            for (int i = 0; i < _behaviour.WanderAttempts; i++)
            {
                int x = c.Column + b.Rng.Range(-r, r + 1);
                int y = c.Row + b.Rng.Range(-r, r + 1);
                if (state.Grid.BlocksMovement(x, y)) continue;
                Vec2 p = new GridCell(x, y).Center;
                if (LastBeastRules.IsInFogSoon(state, defs, p, _behaviour.FogWarnTicks)) continue;
                return p;
            }
            return PickWanderGoal(state.Grid, ref b, from);
        }

        /// <summary>Aim point at a box: its centre moved back towards the bot to just outside its near face, so the
        /// line-of-fire check does not count the box itself.</summary>
        private static Vec2 BoxAimPoint(GridCell box, Vec2 from)
        {
            Vec2 centre = box.Center;
            Vec2 dir = (centre - from).Normalized;
            return centre - dir * BoxFaceOffset;
        }

        private static bool InBoxRange(MatchState state, FighterDef def, Vec2 position, GridCell box, float fireRange)
        {
            Vec2 point = BoxAimPoint(box, position);
            if (Vec2.Distance(position, point) > fireRange) return false;
            return def.Attack.IgnoresWalls || Perception.HasClearShot(state.Grid, position, point);
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
            float fireRange = def.AttackRange * _behaviour.FireRangeFraction;

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
            else if (b.Action == BotAction.BreakBox && IsBox(state, b.BoxCell) && InBoxRange(state, def, position, b.BoxCell, fireRange))
                move = Vec2.Zero; // in range of the box: stop and shoot it
            else if (b.Action == BotAction.CheckBush && Vec2.Distance(position, b.Goal) <= fireRange
                && (def.Attack.IgnoresWalls || Perception.HasClearShot(state.Grid, position, b.Goal)))
                move = Vec2.Zero; // in range of the bush: stop and check it
            else move = FollowPath(slot, position, b.Goal); // an attack target out of sight: its last known position
            if (b.Dodging && FindDodge(state, defs, slot, out Vec2 dodge)) move = dodge;

            // One press per tick: ult, else trick, else attack.
            if (b.UltPending)
            {
                if (target < 0) b.UltPending = false;
                else if (AbilitySystem.CanActivate(in f) && UltSystem.IsCharged(defs, in f) && !f.IsHeld(InputButtons.Ult))
                {
                    b.UltPending = false;
                    ref FighterState t = ref state.Fighters[target];
                    Vec2 toTarget = BotAim.LeadPoint(position, t.Position, t.Velocity, def.Ult, _difficulty[slot].LeadFraction) - position;
                    Vec2 ultAim = BotAim.ForAbility(BotAim.Apply(toTarget, _difficulty[slot].AimSpreadDegrees, ref b.Rng), def.Ult,
                        toTarget.Length);
                    if (!ultAim.IsZero) return new PlayerInput(move, ultAim, InputButtons.Ult);
                }
            }
            if (b.TrickPending && AbilitySystem.CanActivate(in f) && TrickSystem.IsReady(defs, in f, tick) && !f.IsHeld(InputButtons.Trick))
            {
                b.TrickPending = false;
                return def.Trick.UsesMoveDirection
                    ? new PlayerInput(b.TrickDir, Vec2.Zero, InputButtons.Trick)
                    : new PlayerInput(move, b.TrickDir, InputButtons.Trick);
            }

            Vec2 aim;
            if (target >= 0)
            {
                // A bot hiding in a bush only shoots back at an enemy that is already on top of it.
                bool hiding = b.Action == BotAction.HideInBush;
                bool close = Vec2.Distance(position, state.Fighters[target].Position) < _behaviour.LastChargeEnemyDistance;
                if ((!hiding || close) && TryAim(state, defs, slot, target, tick, out aim)) return new PlayerInput(move, aim, InputButtons.Attack);
            }
            else if (b.Action == BotAction.CheckBush && b.MemoryEnemy >= 0 && _memory.LastSeenTick(slot, b.MemoryEnemy) != EnemyMemory.Never)
            {
                if (TryShootAt(state, defs, slot, b.Goal, tick, out aim))
                {
                    _memory.ForgetSighting(slot, b.MemoryEnemy); // checked
                    b.MemoryEnemy = -1;
                    return new PlayerInput(move, aim, InputButtons.Attack);
                }
            }
            else if (b.Action == BotAction.BreakBox && IsBox(state, b.BoxCell) && InBoxRange(state, def, position, b.BoxCell, fireRange)
                && TryShootAt(state, defs, slot, BoxAimPoint(b.BoxCell, position), tick, out aim))
                return new PlayerInput(move, aim, InputButtons.Attack);
            else if (b.BushShotPending && TryShootAt(state, defs, slot, b.BushShotPoint, tick, out aim))
            {
                b.BushShotPending = false;
                return new PlayerInput(move, aim, InputButtons.Attack);
            }
            return new PlayerInput(move, Vec2.Zero, InputButtons.None);
        }

        /// <summary>
        /// Sidestep (F1, Normal): the nearest enemy projectile that reaches the bot within <c>DodgeLookaheadTicks</c> and passes
        /// closer than its radius + the bot's radius + <c>DodgeMarginCells</c> — move away from its line (perpendicular; dead
        /// centre — to the strafe side). False if nothing threatens.
        /// </summary>
        private bool FindDodge(MatchState state, MatchDefs defs, int slot, out Vec2 dodge)
        {
            dodge = Vec2.Zero;
            ref FighterState f = ref state.Fighters[slot];
            BotDifficultyDef d = _difficulty[slot];
            float radius = defs.Fighters[f.DefIndex].Radius;
            float lookSeconds = SimTime.TicksToSeconds(d.DodgeLookaheadTicks);
            float bestAlong = float.PositiveInfinity;
            ProjectileState[] projectiles = state.Projectiles;
            for (int i = 0; i < projectiles.Length; i++)
            {
                ref ProjectileState p = ref projectiles[i];
                if (!p.Active || p.Team == f.Team) continue;
                Vec2 rel = f.Position - p.Position;
                float along = Vec2.Dot(rel, p.Dir);
                if (along <= 0f || along >= bestAlong || along > p.Speed * lookSeconds || along > p.RemainingDistance + radius) continue;
                Vec2 side = rel - p.Dir * along;
                float reach = p.Radius + radius + d.DodgeMarginCells;
                if (side.SqrLength > reach * reach) continue;
                bestAlong = along;
                dodge = side.IsZero ? new Vec2(-p.Dir.Y, p.Dir.X) * _brains[slot].StrafeSign : side.Normalized;
            }
            return bestAlong < float.PositiveInfinity;
        }

        private static bool IsBox(MatchState state, GridCell cell) => state.Grid.Get(cell.Column, cell.Row) == TileType.BerryBox;

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

        /// <summary>The ult travels along a line that walls stop (not over walls, not breaking them, not placed / around the caster).</summary>
        private static bool NeedsClearShot(AbilityDef ult)
        {
            if (ult.IgnoresWalls || ult.BreaksWalls) return false;
            switch (ult.Delivery)
            {
                case DeliveryKind.Lob:
                case DeliveryKind.PlaceArea:
                case DeliveryKind.SelfArea:
                case DeliveryKind.SelfBuff:
                    return false;
                default:
                    return true;
            }
        }

        private bool CanPressAttack(in FighterState f, in BotBrain b, int tick) =>
            f.BurstShotsLeft <= 0 && f.Charges > 0 && !f.IsHeld(InputButtons.Attack) && !f.IsDashing && !f.Stunned && tick >= b.NextShotTick;

        /// <summary>True (and the spread aim direction) if the bot presses the attack at its target this tick.</summary>
        private bool TryAim(MatchState state, MatchDefs defs, int slot, int target, int tick, out Vec2 aim)
        {
            aim = Vec2.Zero;
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];
            if (!CanPressAttack(in f, in b, tick)) return false;
            BotDifficultyDef difficulty = _difficulty[slot];
            if (tick < FirstSeenTick(slot, target) + difficulty.ReactionTicks) return false;

            FighterDef def = defs.Fighters[f.DefIndex];
            ref FighterState t = ref state.Fighters[target];
            float distance = Vec2.Distance(f.Position, t.Position);
            if (distance > def.AttackRange * _behaviour.FireRangeFraction) return false;
            if (!def.Attack.IgnoresWalls && !Perception.HasClearShot(state.Grid, f.Position, t.Position)) return false;
            int damage = FireDecision.EstimateAttackDamage(def, f.Level, defs.Rules);
            if (!FireDecision.ShouldSpendCharge(f.Charges, t.Health, damage, distance, _behaviour.LastChargeEnemyDistance)) return false;

            Vec2 aimPoint = BotAim.LeadPoint(f.Position, t.Position, t.Velocity, def.Attack, difficulty.LeadFraction);
            aim = BotAim.ForAbility(BotAim.Apply(aimPoint - f.Position, difficulty.AimSpreadDegrees, ref b.Rng), def.Attack, distance);
            if (aim.IsZero) return false;
            b.NextShotTick = tick + _behaviour.MinShotIntervalTicks;
            return true;
        }

        /// <summary>A speculative shot at a point (a bush): within range on a clear line, never with the last charge.</summary>
        private bool TryShootAt(MatchState state, MatchDefs defs, int slot, Vec2 point, int tick, out Vec2 aim)
        {
            aim = Vec2.Zero;
            ref BotBrain b = ref _brains[slot];
            ref FighterState f = ref state.Fighters[slot];
            if (!CanPressAttack(in f, in b, tick) || f.Charges < 2) return false;
            FighterDef def = defs.Fighters[f.DefIndex];
            if (Vec2.Distance(f.Position, point) > def.AttackRange * _behaviour.FireRangeFraction) return false;
            if (!def.Attack.IgnoresWalls && !Perception.HasClearShot(state.Grid, f.Position, point)) return false;

            aim = BotAim.ForAbility(BotAim.Apply(point - f.Position, _difficulty[slot].AimSpreadDegrees, ref b.Rng), def.Attack,
                Vec2.Distance(f.Position, point));
            if (aim.IsZero) return false;
            b.NextShotTick = tick + _behaviour.MinShotIntervalTicks;
            return true;
        }
    }
}
