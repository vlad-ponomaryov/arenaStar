using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Delayed circular zones in fixed slots (<see cref="SimLimits.MaxAreas"/>): at <see cref="AreaState.ActivateTick"/> the
    /// ability's effects are applied once to every matching fighter whose body touches the circle; the slot is freed at
    /// <see cref="AreaState.ExpireTick"/>. Created by lobbed bombs and placed zones (S2.11+); views show the zone meanwhile.
    /// </summary>
    public static class AreaSystem
    {
        /// <summary>Creates an area. Returns its slot, or −1 if all slots are busy (counted, no exception).</summary>
        public static int TrySpawn(MatchState state, int owner, int team, Vec2 position, float radius, int activateTick,
            int expireTick, int abilityRef)
        {
            AreaState[] items = state.Areas;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Active) continue;
                ref AreaState a = ref items[i];
                a = default;
                a.Active = true;
                a.Owner = owner;
                a.Team = team;
                a.Position = position;
                a.Radius = radius;
                a.ActivateTick = activateTick;
                a.ExpireTick = expireTick < activateTick ? activateTick : expireTick;
                a.AbilityRef = abilityRef;
                return i;
            }
            state.Globals.DroppedAreas++;
            return -1;
        }

        public static void Run(MatchState state, MatchDefs defs, SimEventBuffer events)
        {
            AreaState[] items = state.Areas;
            int tick = state.Globals.Tick;
            int count = state.Globals.FighterCount;
            for (int i = 0; i < items.Length; i++)
            {
                ref AreaState a = ref items[i];
                if (!a.Active) continue;

                if (!a.Triggered && tick >= a.ActivateTick)
                {
                    a.Triggered = true;
                    AbilityDef ability = defs.GetAbility(a.AbilityRef);
                    events.Add(new SimEvent(SimEventType.AreaTriggered, tick, a.Owner, -1, i, a.Position, Vec2.Zero));
                    if (ability != null)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            if (!EffectSystem.CanAffect(state, ability, a.Owner, a.Team, j)) continue;
                            ref FighterState t = ref state.Fighters[j];
                            float reach = a.Radius + defs.Fighters[t.DefIndex].Radius;
                            if (Vec2.SqrDistance(t.Position, a.Position) > reach * reach) continue;
                            EffectSystem.ApplyAbility(state, defs, events, ability, a.Owner, a.Team, j);
                        }
                    }
                }

                if (tick >= a.ExpireTick) a.Active = false;
            }
        }
    }
}
