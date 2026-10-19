using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;

namespace Arena.Core.Modes
{
    /// <summary>
    /// Rules of the «Last Beast» mode as a tick system next to <see cref="AcornsRules"/> (step 8a, GDD §5.4, Server-Spec §9).
    /// Times are "time at the end of the tick" (<c>now = tick index + 1</c>).
    /// Per tick: a fog step at <see cref="ModeState.NextFogStepTick"/> (from <see cref="LastBeastModeDef.FogStartTicks"/>, then
    /// every <see cref="LastBeastModeDef.FogStepIntervalTicks"/>) moves every side of the safe rectangle in by
    /// <see cref="LastBeastModeDef.FogShrinkCells"/> down to <see cref="LastBeastModeDef.FogMinHalfSize"/>; fog pulses every
    /// <see cref="LastBeastModeDef.FogPulseTicks"/> damage every alive fighter whose centre is outside it (source −1: no ult
    /// charge, no kill credit; per second base + growth × (step − 1)); berries are picked up by touch (slot order) — each adds
    /// <see cref="LastBeastModeDef.BerryDamagePercent"/> damage and <see cref="LastBeastModeDef.BerryHealthPercent"/> max health
    /// (additive, current health grows by the max health gained, plus <see cref="LastBeastModeDef.BerryHealPercent"/> of the max
    /// health per berry; at most <see cref="LastBeastModeDef.MaxBonusBerries"/> berries count, D-131); then places and the end.
    /// Places: a fighter defeated in a tick gets the number of fighters alive before that tick (<see cref="OnDefeat"/>, called
    /// by DamageSystem; fighters out in the same tick share it). One left → place 1, match over. Nobody left (the last ones fell
    /// together) → of those, the one with more damage dealt, then the lower slot, gets place 1. At
    /// <see cref="LastBeastModeDef.MaxDurationTicks"/> the alive fighters are ranked by health, then damage, then slot.
    /// <see cref="ModeState.WinnerTeam"/> = the winner's team (= its slot in a free-for-all setup). No respawn: the mode's
    /// defs set <see cref="MatchDefs.NoRespawn"/>. No allocations.
    /// </summary>
    public static class LastBeastRules
    {
        public const int NoSlot = -1;

        private const int PercentBase = 100;

        public static void Reset(MatchState state, MatchDefs defs)
        {
            BoxSystem.Load(state, defs);
            if (defs.Mode != ModeKind.LastBeast) return;
            LastBeastModeDef lb = defs.LastBeast;
            ref ModeState m = ref state.Mode;
            m.FogStep = 0;
            m.NextFogStepTick = lb.FogStartTicks;
            m.NextFogPulseTick = lb.FogStartTicks + lb.FogPulseTicks;
            SetSafeRect(ref m, state.Globals.MapWidth, state.Globals.MapHeight, 0f, lb.FogMinHalfSize);
            m.AliveCount = CountAlive(state);
        }

        public static void Run(MatchState state, MatchDefs defs, SimEventBuffer events)
        {
            if (defs.Mode != ModeKind.LastBeast || state.Mode.Phase != ModePhase.Playing) return;
            LastBeastModeDef lb = defs.LastBeast;
            int now = state.Globals.Tick + 1;
            ref ModeState m = ref state.Mode;

            if (now >= m.NextFogStepTick)
            {
                m.FogStep++;
                m.NextFogStepTick += lb.FogStepIntervalTicks;
                SetSafeRect(ref m, state.Globals.MapWidth, state.Globals.MapHeight, m.FogStep * lb.FogShrinkCells, lb.FogMinHalfSize);
                events.Add(new SimEvent(SimEventType.FogAdvanced, state.Globals.Tick, -1, -1, m.FogStep,
                    new Vec2(m.SafeMinX, m.SafeMinY), new Vec2(m.SafeMaxX, m.SafeMaxY)));
            }
            if (now >= m.NextFogPulseTick)
            {
                m.NextFogPulseTick += lb.FogPulseTicks;
                if (m.FogStep > 0) FogPulse(state, defs, events, FogPulseDamage(lb, m.FogStep));
            }

            CollectBerries(state, defs, events, now);
            UpdatePlaces(state, defs, events, now);
        }

        /// <summary>A fighter was defeated: its place, and all its berries + <see cref="LastBeastModeDef.ExtraBerriesOnDefeat"/> scatter around it.</summary>
        public static void OnDefeat(MatchState state, MatchDefs defs, SimEventBuffer events, int target)
        {
            if (defs.Mode != ModeKind.LastBeast || state.Mode.Phase != ModePhase.Playing) return;
            LastBeastModeDef lb = defs.LastBeast;
            ref FighterState f = ref state.Fighters[target];
            int place = state.Mode.AliveCount;
            state.Stats[target].Place = place;
            events.Add(new SimEvent(SimEventType.FighterPlaced, state.Globals.Tick, target, -1, place, f.Position, Vec2.Zero));

            int drop = f.Berries + lb.ExtraBerriesOnDefeat;
            f.Berries = 0;
            int now = state.Globals.Tick + 1;
            for (int i = 0; i < drop; i++)
                PickupSystem.Spawn(state, events, PickupKind.Berry, 1, f.Position, lb.BerryDropScatterRadius, lb.ScatterAttempts, now,
                    now + lb.BerryPickupDelayTicks);
        }

        /// <summary>Damage multiplier of the source's berries: 1 + percent × berries / 100 (1 without the mode or a source).</summary>
        public static float DamageMultiplier(MatchState state, MatchDefs defs, int source)
        {
            if (defs.Mode != ModeKind.LastBeast || source < 0) return 1f;
            int berries = BonusBerries(defs.LastBeast, state.Fighters[source].Berries);
            if (berries <= 0) return 1f;
            return 1f + defs.LastBeast.BerryDamagePercent * berries / (float)PercentBase;
        }

        /// <summary>Berries counted for the bonuses: capped by <see cref="LastBeastModeDef.MaxBonusBerries"/> (D-131).</summary>
        public static int BonusBerries(LastBeastModeDef lb, int berries)
        {
            if (berries <= 0 || lb == null) return 0;
            return lb.MaxBonusBerries > 0 && berries > lb.MaxBonusBerries ? lb.MaxBonusBerries : berries;
        }

        /// <summary>Damage bonus of <paramref name="berries"/> in percent (the HUD and the pickup text, D-131).</summary>
        public static int DamageBonusPercent(LastBeastModeDef lb, int berries) =>
            lb == null ? 0 : lb.BerryDamagePercent * BonusBerries(lb, berries);

        /// <summary>Max health of the fighter with its berries: level-scaled base × (1 + percent × berries / 100).</summary>
        public static int MaxHealthWithBerries(MatchDefs defs, in FighterState f)
        {
            int baseMax = defs.Rules.ScaleByLevel(defs.Fighters[f.DefIndex].MaxHealth, f.Level);
            int berries = BonusBerries(defs.LastBeast, f.Berries);
            if (berries <= 0) return baseMax;
            return MathUtil.RoundToInt(baseMax * (1f + defs.LastBeast.BerryHealthPercent * berries / (float)PercentBase));
        }

        /// <summary>Health one fog pulse removes at fog step <paramref name="step"/> (≥ 1).</summary>
        public static int FogPulseDamage(LastBeastModeDef lb, int step)
        {
            int perSecond = lb.FogDamagePerSecond + lb.FogDamageGrowthPerStep * (step - 1);
            return (int)((long)perSecond * lb.FogPulseTicks / SimLimits.TickRate);
        }

        /// <summary>True if the point is outside the current safe rectangle (always false before the first fog step).</summary>
        public static bool IsInFog(MatchState state, Vec2 p)
        {
            ref ModeState m = ref state.Mode;
            if (m.FogStep <= 0) return false;
            return p.X < m.SafeMinX || p.X > m.SafeMaxX || p.Y < m.SafeMinY || p.Y > m.SafeMaxY;
        }

        /// <summary>
        /// True if the point is in the fog now, or will be after the next fog step if that step comes within
        /// <paramref name="withinTicks"/> (bots flee early). False without the mode.
        /// </summary>
        public static bool IsInFogSoon(MatchState state, MatchDefs defs, Vec2 p, int withinTicks)
        {
            if (defs.Mode != ModeKind.LastBeast) return false;
            if (IsInFog(state, p)) return true;
            ref ModeState m = ref state.Mode;
            if (m.NextFogStepTick - (state.Globals.Tick + 1) > withinTicks) return false;
            NextSafeRect(state, defs, out Vec2 min, out Vec2 max);
            return p.X < min.X || p.X > max.X || p.Y < min.Y || p.Y > max.Y;
        }

        /// <summary>The safe rectangle after the next fog step (corners); the whole map's rectangle without the mode.</summary>
        public static void NextSafeRect(MatchState state, MatchDefs defs, out Vec2 min, out Vec2 max)
        {
            if (defs.Mode != ModeKind.LastBeast || defs.LastBeast == null)
            {
                min = Vec2.Zero;
                max = new Vec2(state.Globals.MapWidth, state.Globals.MapHeight);
                return;
            }
            LastBeastModeDef lb = defs.LastBeast;
            ModeState next = default;
            SetSafeRect(ref next, state.Globals.MapWidth, state.Globals.MapHeight, (state.Mode.FogStep + 1) * lb.FogShrinkCells,
                lb.FogMinHalfSize);
            min = new Vec2(next.SafeMinX, next.SafeMinY);
            max = new Vec2(next.SafeMaxX, next.SafeMaxY);
        }

        /// <summary>Centre of the safe rectangle (the map centre: the fog closes in evenly).</summary>
        public static Vec2 SafeCenter(MatchState state) =>
            new Vec2(state.Globals.MapWidth * 0.5f, state.Globals.MapHeight * 0.5f);

        /// <summary>Fighters active and alive now.</summary>
        public static int CountAlive(MatchState state)
        {
            int n = 0;
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
                if (state.Fighters[i].Active && state.Fighters[i].Alive) n++;
            return n;
        }

        /// <summary>Ticks left until the safety time limit (frozen at the end; 0 without the mode).</summary>
        public static int TimeLeftTicks(MatchState state, MatchDefs defs)
        {
            if (defs.Mode != ModeKind.LastBeast) return 0;
            int at = state.Mode.IsEnded ? state.Mode.EndTick : state.Globals.Tick;
            int left = defs.LastBeast.MaxDurationTicks - at;
            return left > 0 ? left : 0;
        }

        private static void SetSafeRect(ref ModeState m, int width, int height, float shrink, float minHalf)
        {
            float halfX = width * 0.5f, halfY = height * 0.5f;
            float hx = Shrunk(halfX, shrink, minHalf), hy = Shrunk(halfY, shrink, minHalf);
            m.SafeMinX = halfX - hx;
            m.SafeMaxX = halfX + hx;
            m.SafeMinY = halfY - hy;
            m.SafeMaxY = halfY + hy;
        }

        private static float Shrunk(float half, float shrink, float minHalf)
        {
            float h = half - shrink;
            float floor = minHalf < half ? minHalf : half;
            return h > floor ? h : floor;
        }

        private static void FogPulse(MatchState state, MatchDefs defs, SimEventBuffer events, int damage)
        {
            if (damage <= 0) return;
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive || !IsInFog(state, f.Position)) continue;
                DamageSystem.ApplyDamage(state, defs, events, -1, i, damage);
            }
        }

        private static void CollectBerries(MatchState state, MatchDefs defs, SimEventBuffer events, int now)
        {
            PickupState[] pickups = state.Pickups;
            int count = state.Globals.FighterCount;
            float pickupRadius = defs.LastBeast.BerryPickupRadius;
            for (int p = 0; p < pickups.Length; p++)
            {
                ref PickupState item = ref pickups[p];
                if (!item.Active || item.Kind != PickupKind.Berry || now < item.PickableFromTick) continue;
                for (int i = 0; i < count; i++)
                {
                    ref FighterState f = ref state.Fighters[i];
                    if (!f.Active || !f.Alive) continue;
                    float reach = defs.Fighters[f.DefIndex].Radius + pickupRadius;
                    if (Vec2.SqrDistance(f.Position, item.Position) > reach * reach) continue;
                    int oldMax = f.MaxHealth;
                    f.Berries += item.Value;
                    f.MaxHealth = MaxHealthWithBerries(defs, in f);
                    if (f.MaxHealth > oldMax) f.Health += f.MaxHealth - oldMax;
                    // D-131: a berry also heals a share of the max health, so the pickup is felt at once.
                    int heal = MathUtil.RoundToInt(f.MaxHealth * defs.LastBeast.BerryHealPercent * item.Value / (float)PercentBase);
                    int healed = f.Health + heal < f.MaxHealth ? heal : f.MaxHealth - f.Health;
                    if (healed > 0)
                    {
                        f.Health += healed;
                        events.Add(new SimEvent(SimEventType.Healed, state.Globals.Tick, i, i, healed, f.Position, Vec2.Zero));
                    }
                    item.Active = false;
                    events.Add(new SimEvent(SimEventType.PickupCollected, state.Globals.Tick, i, -1, item.Value, item.Position, Vec2.Zero));
                    break;
                }
            }
        }

        private static void UpdatePlaces(MatchState state, MatchDefs defs, SimEventBuffer events, int now)
        {
            ref ModeState m = ref state.Mode;
            int before = m.AliveCount;
            int alive = CountAlive(state);
            m.AliveCount = alive;
            int count = state.Globals.FighterCount;

            if (alive == 1)
            {
                for (int i = 0; i < count; i++)
                    if (state.Fighters[i].Active && state.Fighters[i].Alive)
                    {
                        Finish(state, events, i, now);
                        return;
                    }
            }
            if (alive == 0)
            {
                // The last fighters fell in the same tick: they share place `before`; the best of them wins.
                int winner = NoSlot;
                for (int i = 0; i < count; i++)
                {
                    if (!state.Fighters[i].Active || state.Stats[i].Place != before) continue;
                    if (winner == NoSlot || state.Stats[i].DamageDealt > state.Stats[winner].DamageDealt) winner = i;
                }
                Finish(state, events, winner, now);
                return;
            }
            if (now >= defs.LastBeast.MaxDurationTicks) FinishByTimeLimit(state, events, alive, now);
        }

        /// <summary>Time limit: the alive fighters get places 1..alive by health, then damage dealt, then slot.</summary>
        private static void FinishByTimeLimit(MatchState state, SimEventBuffer events, int alive, int now)
        {
            int count = state.Globals.FighterCount;
            int winner = NoSlot;
            for (int place = 1; place <= alive; place++)
            {
                int best = NoSlot;
                for (int i = 0; i < count; i++)
                {
                    ref FighterState f = ref state.Fighters[i];
                    if (!f.Active || !f.Alive || state.Stats[i].Place != 0) continue;
                    if (best == NoSlot || Ranks(state, i, best)) best = i;
                }
                if (best == NoSlot) break;
                state.Stats[best].Place = place;
                if (place == 1) winner = best;
                else events.Add(new SimEvent(SimEventType.FighterPlaced, state.Globals.Tick, best, -1, place, state.Fighters[best].Position, Vec2.Zero));
            }
            Finish(state, events, winner, now);
        }

        /// <summary>True if <paramref name="a"/> ranks strictly above <paramref name="b"/> by health, then damage (equal → the caller keeps the lower slot).</summary>
        private static bool Ranks(MatchState state, int a, int b)
        {
            int ha = state.Fighters[a].Health, hb = state.Fighters[b].Health;
            if (ha != hb) return ha > hb;
            return state.Stats[a].DamageDealt > state.Stats[b].DamageDealt;
        }

        private static void Finish(MatchState state, SimEventBuffer events, int winner, int now)
        {
            ref ModeState m = ref state.Mode;
            m.Phase = ModePhase.Ended;
            m.EndTick = now;
            int team = AcornsRules.NoTeam;
            if (winner != NoSlot)
            {
                state.Stats[winner].Place = 1;
                team = state.Fighters[winner].Team;
                events.Add(new SimEvent(SimEventType.FighterPlaced, state.Globals.Tick, winner, -1, 1, state.Fighters[winner].Position, Vec2.Zero));
            }
            m.WinnerTeam = team;
            events.Add(new SimEvent(SimEventType.MatchEnded, state.Globals.Tick, -1, -1, team, Vec2.Zero, Vec2.Zero));
        }
    }
}
