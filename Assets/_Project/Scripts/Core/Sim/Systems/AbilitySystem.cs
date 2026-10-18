using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Sim.Effects;
using Arena.Core.Sim.Events;
using Arena.Core.State;

namespace Arena.Core.Sim.Systems
{
    /// <summary>
    /// One system for every ability (D-011): attack, ult and trick differ only in their gate — charges and bursts
    /// (<see cref="AttackSystem"/>), ult charge (<see cref="UltSystem"/>), uses and cooldown (<see cref="TrickSystem"/>) —
    /// and all deliver through <see cref="Activate"/> by <see cref="DeliveryKind"/>. Effects come from data.
    ///
    /// Per fighter and tick, in this order: attack (burst continuation or a new press), ult, trick — so an attack and a
    /// trick pressed together both happen. A stunned fighter does nothing (its burst was cancelled by the stun); a dashing
    /// fighter starts nothing new (a burst started before the dash continues).
    /// An ability that may affect enemies counts as attacking: restarts the regen delay, ends invisibility and reveals the
    /// fighter for <see cref="CombatRules.RevealTicks"/> (D-024).
    /// </summary>
    public static class AbilitySystem
    {
        public static void Run(MatchState state, MatchDefs defs, PlayerInput[] inputs, SimEventBuffer events)
        {
            int count = state.Globals.FighterCount;
            for (int i = 0; i < count; i++)
            {
                ref FighterState f = ref state.Fighters[i];
                if (!f.Active || !f.Alive || f.Stunned) continue;
                AttackSystem.Update(state, defs, in inputs[i], events, i);
                UltSystem.TryUse(state, defs, in inputs[i], events, i);
                TrickSystem.TryUse(state, defs, in inputs[i], events, i);
            }
        }

        /// <summary>True if the fighter may start a new ability now (alive, not stunned, not dashing).</summary>
        public static bool CanActivate(in FighterState f) => f.Active && f.Alive && !f.Stunned && !f.IsDashing;

        /// <summary>
        /// Direction of an activation: movement input (else facing) for <see cref="AbilityDef.UsesMoveDirection"/>;
        /// otherwise auto-aim / manual aim / facing within the ability's <see cref="AbilityDef.Range"/> (D-007).
        /// </summary>
        public static Vec2 ResolveDirection(MatchState state, int slot, in PlayerInput input, AbilityDef ability)
        {
            if (ability.UsesMoveDirection)
            {
                if (!input.Move.IsZero) return input.Move.Normalized;
                Vec2 facing = state.Fighters[slot].Facing;
                return facing.IsZero ? Vec2.Up : facing;
            }
            return TargetingSystem.ResolveAim(state, slot, in input, ability.Range);
        }

        /// <summary>Delivers one shot of <paramref name="ability"/> from the fighter in <paramref name="slot"/> along the unit <paramref name="dir"/>.</summary>
        public static void Activate(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, AbilityDef ability,
            int abilityRef, Vec2 dir)
        {
            ref FighterState f = ref state.Fighters[slot];
            int tick = state.Globals.Tick;
            if (ability.Affects(TargetFilter.Enemy))
            {
                RegenSystem.MarkInCombat(ref f, tick);
                f.RevealedUntilTick = tick + defs.Rules.RevealTicks;
                if (f.Invisible) StatusSystem.RemoveKind(state, slot, EffectKind.Invisibility);
            }
            events.Add(new SimEvent(SimEventType.AbilityFired, tick, slot, -1, abilityRef, f.Position, dir));

            switch (ability.Delivery)
            {
                case DeliveryKind.Projectile:
                    FireProjectiles(state, slot, f.Team, f.Position, dir, ability, abilityRef);
                    break;
                case DeliveryKind.MeleeCone:
                    HitCone(state, defs, events, slot, f.Team, f.Position, dir, ability, abilityRef);
                    break;
                case DeliveryKind.Dash:
                    DashSystem.Start(state, slot, ability, abilityRef, dir);
                    break;
                case DeliveryKind.SelfArea:
                    HitSelfArea(state, defs, events, slot, f.Team, f.Position, ability, abilityRef);
                    break;
                case DeliveryKind.SelfBuff:
                    var ctx = new EffectContext(ability, abilityRef, slot, f.Team, f.Position, dir);
                    EffectSystem.ApplyAbility(state, defs, events, in ctx, slot);
                    break;
            }
        }

        /// <summary>
        /// One projectile, or a fan of <see cref="AbilityDef.ProjectileCount"/> spread evenly from −spread/2 to +spread/2
        /// around <paramref name="dir"/> (each projectile hits independently).
        /// </summary>
        public static void FireProjectiles(MatchState state, int slot, int team, Vec2 origin, Vec2 dir, AbilityDef ability, int abilityRef)
        {
            int n = ability.ProjectileCount < 1 ? 1 : ability.ProjectileCount;
            if (n == 1)
            {
                ProjectileSystem.TrySpawn(state, slot, team, origin, dir, ability, abilityRef);
                return;
            }
            float spread = ability.SpreadDegrees;
            float step = spread / (n - 1);
            for (int k = 0; k < n; k++)
                ProjectileSystem.TrySpawn(state, slot, team, origin, MathUtil.Rotate(dir, -spread * 0.5f + step * k), ability, abilityRef);
        }

        /// <summary>
        /// Instant cone: every matching fighter whose centre is within <c>Range + its radius</c> and within half the cone
        /// angle of <paramref name="dir"/> and not behind a projectile-blocking tile (line of sight to its centre).
        /// Knockback pushes away from the origin.
        /// </summary>
        public static void HitCone(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, int team, Vec2 origin,
            Vec2 dir, AbilityDef ability, int abilityRef)
        {
            float cosHalf = MathF.Cos(ability.ConeAngleDegrees * 0.5f * MathUtil.DegToRad);
            var ctx = new EffectContext(ability, abilityRef, slot, team, origin, Vec2.Zero);
            int count = state.Globals.FighterCount;
            TileGrid grid = state.Grid;
            for (int j = 0; j < count; j++)
            {
                if (j == slot || !EffectSystem.CanAffect(state, ability, slot, team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = ability.Range + defs.Fighters[t.DefIndex].Radius;
                Vec2 to = t.Position - origin;
                if (to.SqrLength > reach * reach) continue;
                if (!to.IsZero && Vec2.Dot(to.Normalized, dir) < cosHalf) continue;
                if (!ability.IgnoresWalls && !GridCollision.HasLineOfSight(in grid, origin, t.Position)) continue;
                EffectSystem.ApplyAbility(state, defs, events, in ctx, j);
            }
        }

        /// <summary>
        /// Instant circle of <see cref="AbilityDef.Range"/> around the caster: every matching fighter whose body touches it
        /// (the caster itself if an effect has the Self filter). Knockback pushes away from the caster.
        /// </summary>
        public static void HitSelfArea(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, int team, Vec2 origin,
            AbilityDef ability, int abilityRef)
        {
            var ctx = new EffectContext(ability, abilityRef, slot, team, origin, Vec2.Zero);
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (!EffectSystem.CanAffect(state, ability, slot, team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = ability.Range + defs.Fighters[t.DefIndex].Radius;
                if (Vec2.SqrDistance(t.Position, origin) > reach * reach) continue;
                EffectSystem.ApplyAbility(state, defs, events, in ctx, j);
            }
        }
    }
}
