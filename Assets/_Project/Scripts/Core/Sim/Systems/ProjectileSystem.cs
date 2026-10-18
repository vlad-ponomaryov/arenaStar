using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Straight projectiles in fixed slots (<see cref="SimLimits.MaxProjectiles"/>, D-004). Each tick a projectile moves
    /// <c>speed × dt</c> (not beyond its remaining range); targets touched by the swept segment (segment–circle test,
    /// radius = projectile + body) are hit nearest-first, each at most once (<see cref="ProjectileState.HitMask"/>).
    /// Without pierce left it stops at the hit point. Walls (S2.5) will stop it too; water does not.
    /// </summary>
    public static class ProjectileSystem
    {
        /// <summary>Fires a projectile. Returns its slot, or −1 if all slots are busy (counted, no exception).</summary>
        public static int TrySpawn(MatchState state, int owner, int team, Vec2 position, Vec2 dir, AbilityDef ability, int abilityRef)
        {
            ProjectileState[] items = state.Projectiles;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Active) continue;
                ref ProjectileState p = ref items[i];
                p = default;
                p.Active = true;
                p.Owner = owner;
                p.Team = team;
                p.SpawnTick = state.Globals.Tick;
                p.Position = position;
                p.Dir = dir;
                p.Speed = ability.ProjectileSpeed;
                p.Radius = ability.ProjectileRadius;
                p.RemainingDistance = ability.Range;
                p.AbilityRef = abilityRef;
                p.PierceLeft = ability.PierceCount;
                p.HitMask = owner >= 0 ? 1u << owner : 0u;
                return i;
            }
            state.Globals.DroppedProjectiles++;
            return -1;
        }

        public static void Run(MatchState state, MatchDefs defs, SimEventBuffer events)
        {
            ProjectileState[] items = state.Projectiles;
            int tick = state.Globals.Tick;
            for (int i = 0; i < items.Length; i++)
            {
                ref ProjectileState p = ref items[i];
                if (!p.Active) continue;

                AbilityDef ability = defs.GetAbility(p.AbilityRef);
                float step = p.Speed * SimTime.Dt;
                if (step > p.RemainingDistance) step = p.RemainingDistance;
                Vec2 start = p.Position;

                bool stopped = false;
                while (ability != null)
                {
                    int hit = FindHit(state, defs, ability, in p, start, step, out float along);
                    if (hit < 0) break;

                    p.HitMask |= 1u << hit;
                    Vec2 hitPoint = start + p.Dir * along;
                    events.Add(new SimEvent(SimEventType.ProjectileHit, tick, p.Owner, hit, i, hitPoint, p.Dir));
                    var ctx = new EffectContext(ability, p.AbilityRef, p.Owner, p.Team, hitPoint, p.Dir);
                    EffectSystem.ApplyAbility(state, defs, events, in ctx, hit);
                    if (p.PierceLeft > 0)
                    {
                        p.PierceLeft--;
                        continue;
                    }
                    p.Position = hitPoint;
                    stopped = true;
                    break;
                }

                if (!stopped)
                {
                    p.Position = start + p.Dir * step;
                    p.RemainingDistance -= step;
                    if (p.RemainingDistance > MathUtil.DistanceEpsilon && ability != null) continue;
                }
                p.Active = false;
                events.Add(new SimEvent(SimEventType.ProjectileEnded, tick, p.Owner, -1, i, p.Position, p.Dir));
            }
        }

        /// <summary>Nearest not-yet-hit matching fighter touched by the segment <c>start → start + dir × step</c>, or −1.</summary>
        private static int FindHit(MatchState state, MatchDefs defs, AbilityDef ability, in ProjectileState p, Vec2 start,
            float step, out float bestAlong)
        {
            int count = state.Globals.FighterCount;
            int found = -1;
            bestAlong = 0f;
            for (int j = 0; j < count; j++)
            {
                if ((p.HitMask & (1u << j)) != 0) continue;
                if (!EffectSystem.CanAffect(state, ability, p.Owner, p.Team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = p.Radius + defs.Fighters[t.DefIndex].Radius;
                float along = MathUtil.Clamp(Vec2.Dot(t.Position - start, p.Dir), 0f, step);
                Vec2 closest = start + p.Dir * along;
                if (Vec2.SqrDistance(closest, t.Position) > reach * reach) continue;
                if (found < 0 || along < bestAlong)
                {
                    found = j;
                    bestAlong = along;
                }
            }
            return found;
        }
    }
}
