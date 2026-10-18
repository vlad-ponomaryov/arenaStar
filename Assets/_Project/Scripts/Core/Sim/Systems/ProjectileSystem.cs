using System;
using Arena.Core.Defs;
using Arena.Core.Map;
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
    /// Without pierce left it stops at the hit point. Projectile-blocking tiles (walls, breakable walls) stop it at the
    /// wall face, so targets behind the wall are not hit; water does not. <see cref="AbilityDef.BreaksWalls"/> breaks a
    /// breakable wall and flies on; <see cref="AbilityDef.IgnoresWalls"/> ignores tiles. An enemy field area with
    /// <see cref="AbilityDef.BlocksEnemyProjectiles"/> (Kora's dome) stops a projectile coming from outside at its edge; one
    /// fired inside flies out freely. <see cref="AbilityDef.PierceAll"/> passes every target; with
    /// <see cref="AbilityDef.BounceCount"/> a projectile that would stop jumps from the hit point to the nearest not-yet-hit
    /// fighter it may affect (visible to its team) within <see cref="AbilityDef.BounceRange"/> and flies on towards it (the rest
    /// of that tick's step is dropped). With <see cref="AbilityDef.PierceFalloffPercent"/> every target after the first takes
    /// that much less damage (Nova). A <see cref="DeliveryKind.Hook"/> projectile pulls its owner (<see cref="DashSystem.StartPull"/>)
    /// to the wall face it hits, or next to the enemy it hits (after the effects); a miss or an enemy dome does nothing.
    /// </summary>
    public static class ProjectileSystem
    {
        /// <summary>Fires a projectile. Returns its slot, or −1 if all slots are busy (counted, no exception).</summary>
        public static int TrySpawn(MatchState state, int owner, int team, Vec2 position, Vec2 dir, AbilityDef ability, int abilityRef,
            int damagePercent = EffectContext.FullDamagePercent)
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
                p.PierceLeft = ability.PierceAll ? SimLimits.MaxFighters : ability.PierceCount;
                p.BounceLeft = ability.BounceCount;
                p.HitMask = owner >= 0 ? 1u << owner : 0u;
                p.DamagePercent = damagePercent;
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
                bool tileHit = ability != null && !ability.IgnoresWalls && ClipByWalls(state, events, ability, in p, start, ref step);
                bool wallHit = tileHit;
                if (ClipByFields(state, defs, in p, start, ref step))
                {
                    wallHit = true;
                    tileHit = false; // stopped by a dome before the wall
                }

                bool stopped = false;
                bool bounced = false;
                int stoppedBy = -1;
                while (ability != null)
                {
                    int hit = FindHit(state, defs, ability, in p, start, step, out float along);
                    if (hit < 0) break;

                    p.HitMask |= 1u << hit;
                    Vec2 hitPoint = start + p.Dir * along;
                    events.Add(new SimEvent(SimEventType.ProjectileHit, tick, p.Owner, hit, i, hitPoint, p.Dir));
                    int percent = p.DamagePercent;
                    if (p.Hits > 0 && ability.PierceFalloffPercent > 0) percent = percent * (100 - ability.PierceFalloffPercent) / 100;
                    p.Hits++;
                    var ctx = new EffectContext(ability, p.AbilityRef, p.Owner, p.Team, hitPoint, p.Dir, percent);
                    EffectSystem.ApplyAbility(state, defs, events, in ctx, hit);
                    if (p.PierceLeft > 0)
                    {
                        p.PierceLeft--;
                        continue;
                    }
                    p.Position = hitPoint;
                    if (p.BounceLeft > 0 && TryBounce(state, defs, ability, ref p, hitPoint))
                    {
                        p.BounceLeft--;
                        bounced = true;
                        break;
                    }
                    stopped = true;
                    stoppedBy = hit;
                    break;
                }
                if (bounced) continue;

                if (!stopped)
                {
                    p.Position = start + p.Dir * step;
                    p.RemainingDistance -= step;
                    if (!wallHit && p.RemainingDistance > MathUtil.DistanceEpsilon && ability != null) continue;
                }
                p.Active = false;
                events.Add(new SimEvent(SimEventType.ProjectileEnded, tick, p.Owner, -1, i, p.Position, p.Dir));
                if (ability != null && ability.Delivery == DeliveryKind.Hook)
                {
                    if (stoppedBy >= 0) PullToFighter(state, defs, ability, p.Owner, stoppedBy);
                    else if (tileHit && !stopped) PullToPoint(state, defs, ability, p.Owner, p.Position);
                }
            }
        }

        /// <summary>Hook into an enemy: the owner is pulled until the bodies touch.</summary>
        private static void PullToFighter(MatchState state, MatchDefs defs, AbilityDef ability, int owner, int target)
        {
            if (owner < 0) return;
            ref FighterState o = ref state.Fighters[owner];
            ref FighterState t = ref state.Fighters[target];
            float gap = defs.Fighters[o.DefIndex].Radius + defs.Fighters[t.DefIndex].Radius;
            Vec2 to = t.Position - o.Position;
            float distance = to.Length - gap;
            DashSystem.StartPull(state, owner, to, distance, ability.DashSpeed);
        }

        /// <summary>Hook into a wall: the owner is pulled until its body touches the wall face at <paramref name="point"/>.</summary>
        private static void PullToPoint(MatchState state, MatchDefs defs, AbilityDef ability, int owner, Vec2 point)
        {
            if (owner < 0) return;
            ref FighterState o = ref state.Fighters[owner];
            Vec2 to = point - o.Position;
            float distance = to.Length - defs.Fighters[o.DefIndex].Radius;
            DashSystem.StartPull(state, owner, to, distance, ability.DashSpeed);
        }

        /// <summary>
        /// Shortens <paramref name="step"/> to the first projectile-blocking cell on the way; returns true if a wall stops
        /// the projectile in this tick. Wall breakers break breakable cells and continue.
        /// </summary>
        private static bool ClipByWalls(MatchState state, SimEventBuffer events, AbilityDef ability, in ProjectileState p, Vec2 start, ref float step)
        {
            TileGrid grid = state.Grid;
            while (GridCollision.Raycast(in grid, start, p.Dir, step, out float distance, out GridCell cell))
            {
                if (ability.BreaksWalls && TileSystem.TryBreak(state, events, cell.Column, cell.Row, p.Owner)) continue;
                step = distance;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Shortens <paramref name="step"/> to the edge of the first enemy blocking field the segment enters from outside;
        /// returns true if one stops the projectile in this tick.
        /// </summary>
        private static bool ClipByFields(MatchState state, MatchDefs defs, in ProjectileState p, Vec2 start, ref float step)
        {
            AreaState[] areas = state.Areas;
            int tick = state.Globals.Tick;
            bool blocked = false;
            for (int i = 0; i < areas.Length; i++)
            {
                ref AreaState a = ref areas[i];
                if (!a.Active || a.Kind != AreaKind.Field || a.Team == p.Team || tick < a.ActivateTick) continue;
                if (AreaSystem.Contains(in a, start)) continue; // fired inside: flies out
                AbilityDef field = defs.GetAbility(a.AbilityRef);
                if (field == null || !field.BlocksEnemyProjectiles) continue;
                if (!EntryDistance(start, p.Dir, a.Position, a.Radius, out float t) || t > step) continue;
                step = t;
                blocked = true;
            }
            return blocked;
        }

        /// <summary>Distance along the unit <paramref name="dir"/> from <paramref name="start"/> (outside) to where the ray enters the circle.</summary>
        private static bool EntryDistance(Vec2 start, Vec2 dir, Vec2 centre, float radius, out float t)
        {
            Vec2 m = start - centre;
            float b = Vec2.Dot(m, dir);
            float c = m.SqrLength - radius * radius;
            t = 0f;
            if (b > 0f) return false; // moving away
            float disc = b * b - c;
            if (disc < 0f) return false;
            t = -b - MathF.Sqrt(disc);
            if (t < 0f) t = 0f;
            return true;
        }

        /// <summary>Turns the projectile from <paramref name="from"/> towards the nearest bounce target; false if there is none.</summary>
        private static bool TryBounce(MatchState state, MatchDefs defs, AbilityDef ability, ref ProjectileState p, Vec2 from)
        {
            int count = state.Globals.FighterCount;
            float best = ability.BounceRange * ability.BounceRange;
            int found = -1;
            for (int j = 0; j < count; j++)
            {
                if ((p.HitMask & (1u << j)) != 0) continue;
                if (!EffectSystem.CanAffect(state, ability, p.Owner, p.Team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                if (!t.IsVisibleTo(p.Team)) continue;
                float d = Vec2.SqrDistance(t.Position, from);
                if (d < best || (found < 0 && d <= best))
                {
                    best = d;
                    found = j;
                }
            }
            if (found < 0) return false;
            Vec2 dir = (state.Fighters[found].Position - from).Normalized;
            if (dir.IsZero) return false;
            p.Dir = dir;
            p.RemainingDistance = ability.BounceRange;
            return true;
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
