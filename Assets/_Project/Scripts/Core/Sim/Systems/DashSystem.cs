using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// Dashes (<see cref="DeliveryKind.Dash"/>): the caster covers <see cref="AbilityDef.DashDistance"/> exactly in
    /// <c>round(distance / speed × 30)</c> ticks (at least 1), starting in the tick of activation, ignoring its own
    /// movement input. Every matching fighter whose body touches the swept segment gets the ability's effects once
    /// (<see cref="FighterState.DashHitMask"/>); the delivery direction is the dash direction (knockback pushes forward).
    /// Collides with walls and water like walking (slides along them); with <see cref="AbilityDef.BreaksWalls"/> every
    /// breakable wall the body touches is broken first (Bruno's ram). Cancelled by stun, knockback, defeat.
    /// While dashing a fighter starts no other ability (<see cref="AbilitySystem.CanActivate"/>).
    /// </summary>
    public static class DashSystem
    {
        public static void Start(MatchState state, int slot, AbilityDef ability, int abilityRef, Vec2 dir)
        {
            ref FighterState f = ref state.Fighters[slot];
            int ticks = MathUtil.Max(1, MathUtil.RoundToInt(ability.DashDistance / ability.DashSpeed * SimLimits.TickRate));
            f.DashVelocity = dir * (ability.DashDistance / (ticks * SimTime.Dt));
            f.DashTicksLeft = ticks;
            f.DashAbilityRef = abilityRef;
            f.DashHitMask = 1u << slot;
            f.Facing = dir;
            // A dash overrides a push in progress (the roll gets you out of it).
            f.KnockbackTicks = 0;
            f.KnockbackVelocity = Vec2.Zero;
        }

        /// <summary>
        /// Pull of a hook (<see cref="DeliveryKind.Hook"/>): the fighter moves <paramref name="distance"/> cells along
        /// <paramref name="direction"/> at <paramref name="speed"/> like an effect-less dash (walls and water stop it; stun,
        /// knockback, defeat cancel it). Nothing happens if the fighter cannot move now (defeated, stunned) or the distance is ~0.
        /// </summary>
        public static void StartPull(MatchState state, int slot, Vec2 direction, float distance, float speed)
        {
            ref FighterState f = ref state.Fighters[slot];
            if (!f.Active || !f.Alive || f.Stunned || speed <= 0f || distance <= MathUtil.DistanceEpsilon) return;
            Vec2 dir = direction.Normalized;
            if (dir.IsZero) return;
            int ticks = MathUtil.Max(1, MathUtil.RoundToInt(distance / speed * SimLimits.TickRate));
            f.DashVelocity = dir * (distance / (ticks * SimTime.Dt));
            f.DashTicksLeft = ticks;
            f.DashAbilityRef = -1;
            f.DashHitMask = 1u << slot;
            f.Facing = dir;
            f.KnockbackTicks = 0;
            f.KnockbackVelocity = Vec2.Zero;
        }

        public static void Cancel(ref FighterState f)
        {
            f.DashTicksLeft = 0;
            f.DashVelocity = Vec2.Zero;
            f.DashAbilityRef = -1;
            f.DashHitMask = 0u;
        }

        public static void Run(MatchState state, MatchDefs defs, SimEventBuffer events)
        {
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive || f.DashTicksLeft <= 0) continue;

                float radius = defs.Fighters[f.DefIndex].Radius;
                Vec2 start = f.Position;
                Vec2 dir = f.DashVelocity.Normalized;
                AbilityDef ability = defs.GetAbility(f.DashAbilityRef);
                f.Position = Move(state, events, i, start, f.DashVelocity * SimTime.Dt, radius, ability != null && ability.BreaksWalls);
                f.Velocity = (f.Position - start) * SimLimits.TickRate;
                f.DashTicksLeft--;

                if (ability != null && ability.Effects.Length > 0)
                    HitAlong(state, defs, events, i, ability, f.DashAbilityRef, start, f.Position, dir, radius);

                if (f.DashTicksLeft <= 0) Cancel(ref f);
            }
        }

        /// <summary>Sub-stepped move with collision; a wall breaker breaks touched breakable walls before colliding.</summary>
        private static Vec2 Move(MatchState state, SimEventBuffer events, int slot, Vec2 from, Vec2 delta, float radius, bool breaksWalls)
        {
            TileGrid grid = state.Grid;
            if (!breaksWalls) return GridCollision.MoveCircle(in grid, from, delta, radius);
            int steps = GridCollision.SubstepCount(delta.Length);
            Vec2 step = delta / steps;
            Vec2 p = from;
            for (int k = 0; k < steps; k++)
            {
                p += step;
                TileSystem.BreakOverlapping(state, events, p, radius, slot);
                p = GridCollision.ResolveCircle(in grid, p, radius);
            }
            return p;
        }

        private static void HitAlong(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, AbilityDef ability,
            int abilityRef, Vec2 start, Vec2 end, Vec2 dir, float radius)
        {
            ref FighterState f = ref state.Fighters[slot];
            Vec2 seg = end - start;
            float length = seg.Length;
            Vec2 segDir = length > MathUtil.Epsilon ? seg / length : Vec2.Zero;
            var ctx = new EffectContext(ability, abilityRef, slot, f.Team, end, dir);
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if ((f.DashHitMask & (1u << j)) != 0) continue;
                if (!EffectSystem.CanAffect(state, ability, slot, f.Team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = radius + defs.Fighters[t.DefIndex].Radius;
                float along = MathUtil.Clamp(Vec2.Dot(t.Position - start, segDir), 0f, length);
                Vec2 closest = start + segDir * along;
                if (Vec2.SqrDistance(closest, t.Position) > reach * reach) continue;
                f.DashHitMask |= 1u << j;
                EffectSystem.ApplyAbility(state, defs, events, in ctx, j);
            }
        }
    }
}
