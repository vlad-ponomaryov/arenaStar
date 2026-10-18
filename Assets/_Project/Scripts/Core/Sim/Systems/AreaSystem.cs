using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Circular zones in fixed slots (<see cref="SimLimits.MaxAreas"/>), created by lobs and placed abilities
    /// (<see cref="AbilitySystem.SpawnArea"/>). A fighter is inside if its body touches the circle. By <see cref="AreaKind"/>:
    ///   - burst: at <see cref="AreaState.ActivateTick"/> the ability's effects apply once to everyone matching inside;
    ///   - mine: from the activation until <see cref="AreaState.ExpireTick"/>, the first enemy inside sets it off — effects
    ///     once to everyone matching inside, then the slot is freed; unused, it just expires;
    ///   - field: a pulse every <see cref="AbilityDef.AreaPulseTicks"/> (the first one interval after the activation) applies
    ///     the effects to everyone matching inside, damage/heal split evenly over the pulses of the duration.
    /// A field with <see cref="AbilityDef.BlocksEnemyProjectiles"/> stops enemy projectiles in <see cref="ProjectileSystem"/>.
    /// The slot is freed at <see cref="AreaState.ExpireTick"/> (after that tick's pulse). Views show and telegraph them.
    /// </summary>
    public static class AreaSystem
    {
        /// <summary>Creates a burst area at <paramref name="position"/> (tests, older callers).</summary>
        public static int TrySpawn(MatchState state, int owner, int team, Vec2 position, float radius, int activateTick,
            int expireTick, int abilityRef) =>
            TrySpawn(state, owner, team, position, radius, activateTick, expireTick, abilityRef, AreaKind.Burst, position);

        /// <summary>Creates an area. Returns its slot, or −1 if all slots are busy (counted, no exception).</summary>
        public static int TrySpawn(MatchState state, int owner, int team, Vec2 position, float radius, int activateTick,
            int expireTick, int abilityRef, AreaKind kind, Vec2 origin)
        {
            AreaState[] items = state.Areas;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Active) continue;
                ref AreaState a = ref items[i];
                a = default;
                a.Active = true;
                a.Kind = kind;
                a.Owner = owner;
                a.Team = team;
                a.Position = position;
                a.Origin = origin;
                a.SpawnTick = state.Globals.Tick;
                a.Radius = radius;
                a.ActivateTick = activateTick;
                a.ExpireTick = expireTick < activateTick ? activateTick : expireTick;
                a.AbilityRef = abilityRef;
                a.VisibleToTeams = ~0u;
                return i;
            }
            state.Globals.DroppedAreas++;
            return -1;
        }

        public static void Run(MatchState state, MatchDefs defs, SimEventBuffer events)
        {
            AreaState[] items = state.Areas;
            int tick = state.Globals.Tick;
            for (int i = 0; i < items.Length; i++)
            {
                ref AreaState a = ref items[i];
                if (!a.Active) continue;
                AbilityDef ability = defs.GetAbility(a.AbilityRef);

                if (tick >= a.ActivateTick)
                {
                    switch (a.Kind)
                    {
                        case AreaKind.Burst:
                            if (!a.Triggered) Trigger(state, defs, events, ref a, i, ability);
                            break;
                        case AreaKind.Mine:
                            if (!a.Triggered && ability != null && EnemyInside(state, defs, in a))
                            {
                                Trigger(state, defs, events, ref a, i, ability);
                                a.Active = false;
                                continue;
                            }
                            break;
                        case AreaKind.Field:
                            Pulse(state, defs, events, ref a, ability, tick);
                            break;
                    }
                }

                if (tick >= a.ExpireTick) a.Active = false;
            }
        }

        /// <summary>Number of effect pulses of a field over its duration (≥ 1).</summary>
        public static int PulseCount(AbilityDef ability) =>
            ability == null || ability.AreaPulseTicks <= 0 ? 1 : MathUtil.Max(1, ability.AreaDurationTicks / ability.AreaPulseTicks);

        /// <summary>True if <paramref name="p"/> is strictly inside the circle of an active area (projectile blocking).</summary>
        public static bool Contains(in AreaState a, Vec2 p) => Vec2.SqrDistance(p, a.Position) < a.Radius * a.Radius;

        private static void Trigger(MatchState state, MatchDefs defs, SimEventBuffer events, ref AreaState a, int slot, AbilityDef ability)
        {
            a.Triggered = true;
            events.Add(new SimEvent(SimEventType.AreaTriggered, state.Globals.Tick, a.Owner, -1, slot, a.Position, Vec2.Zero));
            if (ability == null) return;
            var ctx = new EffectContext(ability, a.AbilityRef, a.Owner, a.Team, a.Position, Vec2.Zero);
            ApplyInside(state, defs, events, in a, ability, in ctx);
        }

        private static void Pulse(MatchState state, MatchDefs defs, SimEventBuffer events, ref AreaState a, AbilityDef ability, int tick)
        {
            if (ability == null || ability.AreaPulseTicks <= 0) return;
            int pulses = PulseCount(ability);
            if (a.PulseIndex >= pulses || tick < a.NextPulseTick) return;
            var ctx = new EffectContext(ability, a.AbilityRef, a.Owner, a.Team, a.Position, Vec2.Zero, a.PulseIndex, pulses);
            ApplyInside(state, defs, events, in a, ability, in ctx);
            a.PulseIndex++;
            a.NextPulseTick += ability.AreaPulseTicks;
        }

        private static void ApplyInside(MatchState state, MatchDefs defs, SimEventBuffer events, in AreaState a, AbilityDef ability,
            in EffectContext ctx)
        {
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (!EffectSystem.CanAffect(state, ability, a.Owner, a.Team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = a.Radius + defs.Fighters[t.DefIndex].Radius;
                if (Vec2.SqrDistance(t.Position, a.Position) > reach * reach) continue;
                EffectSystem.ApplyAbility(state, defs, events, in ctx, j);
            }
        }

        /// <summary>An alive enemy of the owner's team whose body touches the area.</summary>
        private static bool EnemyInside(MatchState state, MatchDefs defs, in AreaState a)
        {
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                ref FighterState t = ref state.Fighters[j];
                if (!t.Active || !t.Alive || t.Team == a.Team) continue;
                float reach = a.Radius + defs.Fighters[t.DefIndex].Radius;
                if (Vec2.SqrDistance(t.Position, a.Position) <= reach * reach) return true;
            }
            return false;
        }
    }
}
