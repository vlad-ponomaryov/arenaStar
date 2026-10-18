using Arena.Core.Defs;
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
    /// Stopped by the arena edge (walls and <see cref="AbilityDef.BreaksWalls"/> — S2.5). Cancelled by stun, knockback, defeat.
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
                f.Position = defs.Bounds.ClampCircle(start + f.DashVelocity * SimTime.Dt, radius);
                f.Velocity = (f.Position - start) * SimLimits.TickRate;
                f.DashTicksLeft--;

                AbilityDef ability = defs.GetAbility(f.DashAbilityRef);
                if (ability != null && ability.Effects.Length > 0)
                    HitAlong(state, defs, events, i, ability, f.DashAbilityRef, start, f.Position, dir, radius);

                if (f.DashTicksLeft <= 0) Cancel(ref f);
            }
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
