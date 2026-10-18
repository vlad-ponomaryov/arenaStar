using System;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Modes
{
    /// <summary>
    /// Rules of the Acorns mode as a tick system (GDD §5.4, D-025). Times are "time at the end of the tick"
    /// (<c>now = tick index + 1</c>), so things scheduled at N happen after exactly N calls of <c>Tick</c>.
    /// Per tick (after respawn): a normal acorn every <see cref="AcornsModeDef.SpawnIntervalTicks"/> and a golden one in
    /// addition every <see cref="AcornsModeDef.GoldenIntervalTicks"/>, both scattered around the hollow; pickup by touch
    /// (slot order: the first touching fighter takes it); hold countdown for the team with more acorns if it has
    /// <see cref="AcornsModeDef.TargetAcorns"/>+ (equal 12+ resets it); win when the countdown runs out; at
    /// <see cref="AcornsModeDef.DurationTicks"/> the team with more acorns wins, equal = draw. A defeated fighter drops
    /// all its acorns as normal ones (<see cref="OnDefeat"/>, called by DamageSystem) that wait
    /// <see cref="AcornsModeDef.PickupDelayTicks"/>. After the end the simulation stands still (MatchSimulation).
    /// </summary>
    public static class AcornsRules
    {
        public const int NoTeam = -1;

        public static void Reset(MatchState state, MatchDefs defs)
        {
            ref ModeState m = ref state.Mode;
            m = default;
            m.Phase = ModePhase.Playing;
            m.CountdownTeam = NoTeam;
            m.WinnerTeam = NoTeam;
            if (defs.Mode != ModeKind.Acorns) return;
            m.NextSpawnTick = defs.Acorns.SpawnIntervalTicks;
            m.NextGoldenTick = defs.Acorns.GoldenIntervalTicks;
        }

        public static void Run(MatchState state, MatchDefs defs, SimEventBuffer events)
        {
            if (defs.Mode != ModeKind.Acorns || state.Mode.Phase != ModePhase.Playing) return;
            AcornsModeDef a = defs.Acorns;
            int now = state.Globals.Tick + 1;
            ref ModeState m = ref state.Mode;
            Vec2 hollow = defs.Map.Hollow.Center;

            if (now >= m.NextSpawnTick)
            {
                PickupSystem.Spawn(state, events, PickupKind.Acorn, 1, hollow, a.SpawnScatterRadius, a.ScatterAttempts, now, now);
                m.NextSpawnTick += a.SpawnIntervalTicks;
            }
            if (now >= m.NextGoldenTick)
            {
                PickupSystem.Spawn(state, events, PickupKind.Acorn, a.GoldenValue, hollow, a.SpawnScatterRadius, a.ScatterAttempts, now, now);
                m.NextGoldenTick += a.GoldenIntervalTicks;
            }

            CollectPickups(state, defs, events, now);
            UpdateCountdown(state, a, events, now);
        }

        /// <summary>A fighter was defeated: all its acorns scatter around it as normal ones (D-025). Called by DamageSystem.</summary>
        public static void OnDefeat(MatchState state, MatchDefs defs, SimEventBuffer events, int target)
        {
            if (defs.Mode != ModeKind.Acorns || state.Mode.Phase != ModePhase.Playing) return;
            ref FighterState f = ref state.Fighters[target];
            int carried = f.CarriedAcorns;
            f.CarriedAcorns = 0;
            if (carried <= 0) return;
            AcornsModeDef a = defs.Acorns;
            int now = state.Globals.Tick + 1;
            for (int i = 0; i < carried; i++)
                PickupSystem.Spawn(state, events, PickupKind.Acorn, 1, f.Position, a.DropScatterRadius, a.ScatterAttempts, now, now + a.PickupDelayTicks);
            events.Add(new SimEvent(SimEventType.AcornsDropped, state.Globals.Tick, target, -1, carried, f.Position, Vec2.Zero));
        }

        /// <summary>Acorns carried by the active fighters of <paramref name="team"/>.</summary>
        public static int TeamAcorns(MatchState state, int team)
        {
            int sum = 0;
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (f.Active && f.Team == team) sum += f.CarriedAcorns;
            }
            return sum;
        }

        /// <summary>Ticks left of the hold countdown (0 if none runs).</summary>
        public static int CountdownTicksLeft(MatchState state)
        {
            ref ModeState m = ref state.Mode;
            if (m.CountdownTeam == NoTeam) return 0;
            int left = m.CountdownEndTick - state.Globals.Tick;
            return left > 0 ? left : 0;
        }

        /// <summary>Ticks left until the time limit (frozen at the end; 0 without the mode).</summary>
        public static int TimeLeftTicks(MatchState state, MatchDefs defs)
        {
            if (defs.Mode != ModeKind.Acorns) return 0;
            int at = state.Mode.IsEnded ? state.Mode.EndTick : state.Globals.Tick;
            int left = defs.Acorns.DurationTicks - at;
            return left > 0 ? left : 0;
        }

        private static void CollectPickups(MatchState state, MatchDefs defs, SimEventBuffer events, int now)
        {
            PickupState[] pickups = state.Pickups;
            int count = state.Globals.FighterCount;
            float pickupRadius = defs.Acorns.PickupRadius;
            for (int p = 0; p < pickups.Length; p++)
            {
                ref PickupState item = ref pickups[p];
                if (!item.Active || item.Kind != PickupKind.Acorn || now < item.PickableFromTick) continue;
                for (int i = 0; i < count; i++)
                {
                    ref FighterState f = ref state.Fighters[i];
                    if (!f.Active || !f.Alive) continue;
                    float reach = defs.Fighters[f.DefIndex].Radius + pickupRadius;
                    if (Vec2.SqrDistance(f.Position, item.Position) > reach * reach) continue;
                    f.CarriedAcorns += item.Value;
                    state.Stats[i].AcornsCollected += item.Value;
                    item.Active = false;
                    events.Add(new SimEvent(SimEventType.PickupCollected, state.Globals.Tick, i, -1, item.Value, item.Position,
                        Vec2.Zero));
                    break;
                }
            }
        }

        private static void UpdateCountdown(MatchState state, AcornsModeDef a, SimEventBuffer events, int now)
        {
            ref ModeState m = ref state.Mode;
            int team0 = TeamAcorns(state, 0);
            int team1 = TeamAcorns(state, 1);
            m.Team0Acorns = team0;
            m.Team1Acorns = team1;
            int leader = team0 > team1 ? 0 : (team1 > team0 ? 1 : NoTeam);
            int leaderAcorns = team0 > team1 ? team0 : team1;
            int tick = state.Globals.Tick;

            if (leader != NoTeam && leaderAcorns >= a.TargetAcorns)
            {
                if (m.CountdownTeam != leader)
                {
                    m.CountdownTeam = leader;
                    m.CountdownEndTick = now + a.HoldTicks;
                    events.Add(new SimEvent(SimEventType.CountdownStarted, tick, -1, -1, leader, Vec2.Zero, Vec2.Zero));
                }
                if (now >= m.CountdownEndTick)
                {
                    End(state, events, leader, now);
                    return;
                }
            }
            else if (m.CountdownTeam != NoTeam)
            {
                events.Add(new SimEvent(SimEventType.CountdownReset, tick, -1, -1, m.CountdownTeam, Vec2.Zero, Vec2.Zero));
                m.CountdownTeam = NoTeam;
                m.CountdownEndTick = 0;
            }

            if (now >= a.DurationTicks) End(state, events, leader, now);
        }

        private static void End(MatchState state, SimEventBuffer events, int winner, int now)
        {
            ref ModeState m = ref state.Mode;
            m.Phase = ModePhase.Ended;
            m.EndTick = now;
            m.WinnerTeam = winner;
            events.Add(new SimEvent(SimEventType.MatchEnded, state.Globals.Tick, -1, -1, winner, Vec2.Zero, Vec2.Zero));
        }
    }
}
