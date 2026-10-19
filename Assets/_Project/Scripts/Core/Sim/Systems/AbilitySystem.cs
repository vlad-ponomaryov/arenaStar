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
                if (!state.Globals.Simulates(i)) continue;
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
        /// otherwise auto-aim / manual aim / facing within the ability's <see cref="AbilityDef.Range"/> (D-007). The auto-aim of
        /// an instant contact delivery (<see cref="IsRewound"/>) looks at the targets rewound by the input's
        /// <c>ViewDelayTicks</c> — the same positions its hit test uses (<see cref="LagCompensation"/>).
        /// </summary>
        public static Vec2 ResolveDirection(MatchState state, CombatRules rules, int slot, in PlayerInput input, AbilityDef ability)
        {
            if (ability.UsesMoveDirection)
            {
                if (!input.Move.IsZero) return input.Move.Normalized;
                Vec2 facing = state.Fighters[slot].Facing;
                return facing.IsZero ? Vec2.Up : facing;
            }
            int rewind = IsRewound(ability.Delivery) ? LagCompensation.EffectiveRewind(state, input.ViewDelayTicks) : 0;
            return TargetingSystem.ResolveAim(state, rules, slot, in input, ability.Range, NeedsClearLine(ability), rewind);
        }

        /// <summary>
        /// <see cref="AimTargetCode"/> the auto-aim of <paramref name="ability"/> would pick now (D-007, D-129) and the point it
        /// aims at: the same choice as <see cref="ResolveDirection"/> (views show it while the attack stick is held still).
        /// </summary>
        public static int AutoAimTarget(MatchState state, CombatRules rules, int slot, AbilityDef ability, out Vec2 point,
            int rewindTicks = 0) =>
            TargetingSystem.FindAutoAimTarget(state, rules, slot, ability.Range, NeedsClearLine(ability), rewindTicks, out point);

        /// <summary>
        /// Walls stop this ability on the way to the target (projectile, cone, dash, hook without <see cref="AbilityDef.IgnoresWalls"/>):
        /// its auto-aim needs a clear line (D-129); a lob, an area around the caster or a placed area does not.
        /// </summary>
        public static bool NeedsClearLine(AbilityDef ability) =>
            ability != null && !ability.IgnoresWalls && IsWallStopped(ability.Delivery);

        public static bool IsWallStopped(DeliveryKind delivery) =>
            delivery == DeliveryKind.Projectile || delivery == DeliveryKind.MeleeCone || delivery == DeliveryKind.Dash
            || delivery == DeliveryKind.Hook;

        /// <summary>Instant contact deliveries whose targets are rewound for lag compensation (Server-Spec §2.8): cone, self area, dash.</summary>
        public static bool IsRewound(DeliveryKind delivery) =>
            delivery == DeliveryKind.MeleeCone || delivery == DeliveryKind.SelfArea || delivery == DeliveryKind.Dash;

        /// <summary>
        /// Throw distance of a <see cref="DeliveryKind.Lob"/> (cells, ≤ <see cref="AbilityDef.Range"/>): auto-aim — the
        /// distance to the auto-aim target, manual aim — aim length × range (D-006), no aim — the full range. Other deliveries:
        /// the range.
        /// </summary>
        public static float ResolveDistance(MatchState state, CombatRules rules, int slot, in PlayerInput input, AbilityDef ability)
        {
            if (ability.Delivery != DeliveryKind.Lob) return ability.Range;
            if (input.Has(InputButtons.AutoAim))
            {
                int target = AutoAimTarget(state, rules, slot, ability, out Vec2 point);
                if (target == AimTargetCode.None) return ability.Range;
                float d = Vec2.Distance(point, state.Fighters[slot].Position);
                return MathUtil.Min(d, ability.Range);
            }
            if (!input.Aim.IsZero) return MathUtil.Min(input.Aim.Length, 1f) * ability.Range;
            return ability.Range;
        }

        /// <summary>Delivers one shot of <paramref name="ability"/> from the fighter in <paramref name="slot"/> along the unit
        /// <paramref name="dir"/>; <paramref name="distance"/> is the throw distance of a lob (negative = full range);
        /// <paramref name="damagePercent"/> scales its damage (a boosted attack shot). Dashes ignore the percent.
        /// <paramref name="viewDelayTicks"/> — the input's <c>ViewDelayTicks</c>: a cone or a self area tests rewound targets
        /// (<see cref="LagCompensation"/>; dash touches take it from each tick's input in <see cref="DashSystem.Run"/>).</summary>
        public static void Activate(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, AbilityDef ability,
            int abilityRef, Vec2 dir, float distance = -1f, int damagePercent = EffectContext.FullDamagePercent, int viewDelayTicks = 0)
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
                    FireProjectiles(state, slot, f.Team, f.Position, dir, ability, abilityRef, damagePercent);
                    break;
                case DeliveryKind.Hook:
                    ProjectileSystem.TrySpawn(state, slot, f.Team, f.Position, dir, ability, abilityRef, damagePercent);
                    break;
                case DeliveryKind.MeleeCone:
                    HitCone(state, defs, events, slot, f.Team, f.Position, dir, ability, abilityRef, damagePercent,
                        LagCompensation.EffectiveRewind(state, viewDelayTicks));
                    break;
                case DeliveryKind.Dash:
                    DashSystem.Start(state, slot, ability, abilityRef, dir);
                    break;
                case DeliveryKind.SelfArea:
                    HitSelfArea(state, defs, events, slot, f.Team, f.Position, ability, abilityRef, damagePercent,
                        LagCompensation.EffectiveRewind(state, viewDelayTicks));
                    break;
                case DeliveryKind.SelfBuff:
                    var ctx = new EffectContext(ability, abilityRef, slot, f.Team, f.Position, dir, damagePercent);
                    EffectSystem.ApplyAbility(state, defs, events, in ctx, slot);
                    break;
                case DeliveryKind.Lob:
                    float d = distance < 0f || distance > ability.Range ? ability.Range : distance;
                    Vec2 point = GridCollision.ClampToMap(f.Position + dir * d, MathUtil.DistanceEpsilon, state.Globals.MapWidth, state.Globals.MapHeight);
                    SpawnArea(state, defs, slot, f.Position, point, ability, abilityRef, damagePercent);
                    break;
                case DeliveryKind.PlaceArea:
                    SpawnArea(state, defs, slot, f.Position, f.Position, ability, abilityRef, damagePercent);
                    break;
            }
        }

        /// <summary>
        /// Area of a lob / placed ability at <paramref name="point"/>: radius × the caster's area talent, live after
        /// <see cref="AbilityDef.AreaDelayTicks"/>; a mine / field lasts <see cref="AbilityDef.AreaDurationTicks"/> more.
        /// Returns the area slot, or −1 if all slots are busy (counted).
        /// </summary>
        public static int SpawnArea(MatchState state, MatchDefs defs, int slot, Vec2 origin, Vec2 point, AbilityDef ability, int abilityRef,
            int damagePercent = EffectContext.FullDamagePercent)
        {
            ref FighterState f = ref state.Fighters[slot];
            int tick = state.Globals.Tick;
            float radius = ability.AreaRadius * TalentSystem.AreaRadiusMultiplier(state, defs, slot);
            AreaKind kind = ability.CreatedAreaKind;
            int activate = tick + ability.AreaDelayTicks;
            int expire = kind == AreaKind.Burst ? activate : activate + ability.AreaDurationTicks;
            int area = AreaSystem.TrySpawn(state, slot, f.Team, point, radius, activate, expire, abilityRef, kind, origin);
            if (area >= 0 && kind == AreaKind.Field) state.Areas[area].NextPulseTick = activate + ability.AreaPulseTicks;
            if (area >= 0) state.Areas[area].DamagePercent = damagePercent;
            return area;
        }

        /// <summary>
        /// One projectile, or a fan of <see cref="AbilityDef.ProjectileCount"/> spread evenly from −spread/2 to +spread/2
        /// around <paramref name="dir"/> (each projectile hits independently).
        /// </summary>
        public static void FireProjectiles(MatchState state, int slot, int team, Vec2 origin, Vec2 dir, AbilityDef ability, int abilityRef,
            int damagePercent = EffectContext.FullDamagePercent)
        {
            int n = ability.ProjectileCount < 1 ? 1 : ability.ProjectileCount;
            if (n == 1)
            {
                ProjectileSystem.TrySpawn(state, slot, team, origin, dir, ability, abilityRef, damagePercent);
                return;
            }
            float spread = ability.SpreadDegrees;
            float step = spread / (n - 1);
            for (int k = 0; k < n; k++)
                ProjectileSystem.TrySpawn(state, slot, team, origin, MathUtil.Rotate(dir, -spread * 0.5f + step * k), ability, abilityRef,
                    damagePercent);
        }

        /// <summary>
        /// Instant cone: every matching fighter whose centre is within <c>Range + its radius</c> and within half the cone
        /// angle of <paramref name="dir"/> and not behind a projectile-blocking tile (line of sight to its centre).
        /// Knockback pushes away from the origin. Fighters are tested where they were <paramref name="rewindTicks"/> ago
        /// (<see cref="LagCompensation.TargetPosition"/>; 0 — now), walls and boxes — as they are now.
        /// </summary>
        public static void HitCone(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, int team, Vec2 origin,
            Vec2 dir, AbilityDef ability, int abilityRef, int damagePercent = EffectContext.FullDamagePercent, int rewindTicks = 0)
        {
            float cosHalf = MathF.Cos(ability.ConeAngleDegrees * 0.5f * MathUtil.DegToRad);
            var ctx = new EffectContext(ability, abilityRef, slot, team, origin, Vec2.Zero, damagePercent);
            int count = state.Globals.FighterCount;
            TileGrid grid = state.Grid;
            for (int j = 0; j < count; j++)
            {
                if (j == slot || !EffectSystem.CanAffect(state, ability, slot, team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = ability.Range + defs.Fighters[t.DefIndex].Radius;
                Vec2 position = LagCompensation.TargetPosition(state, j, rewindTicks);
                Vec2 to = position - origin;
                if (to.SqrLength > reach * reach) continue;
                if (!to.IsZero && Vec2.Dot(to.Normalized, dir) < cosHalf) continue;
                if (!ability.IgnoresWalls && !GridCollision.HasLineOfSight(in grid, origin, position)) continue;
                EffectSystem.ApplyAbility(state, defs, events, in ctx, j);
            }
            BoxSystem.DamageCone(state, defs, events, in ctx, origin, dir, ability.Range, cosHalf, ability.IgnoresWalls);
        }

        /// <summary>
        /// Instant circle of <see cref="AbilityDef.Range"/> around the caster: every matching fighter whose body touches it
        /// (the caster itself if an effect has the Self filter). Knockback pushes away from the caster. Other fighters are tested
        /// where they were <paramref name="rewindTicks"/> ago (<see cref="LagCompensation.TargetPosition"/>; 0 — now).
        /// </summary>
        public static void HitSelfArea(MatchState state, MatchDefs defs, SimEventBuffer events, int slot, int team, Vec2 origin,
            AbilityDef ability, int abilityRef, int damagePercent = EffectContext.FullDamagePercent, int rewindTicks = 0)
        {
            var ctx = new EffectContext(ability, abilityRef, slot, team, origin, Vec2.Zero, damagePercent);
            int count = state.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (!EffectSystem.CanAffect(state, ability, slot, team, j)) continue;
                ref FighterState t = ref state.Fighters[j];
                float reach = ability.Range + defs.Fighters[t.DefIndex].Radius;
                Vec2 position = j == slot ? t.Position : LagCompensation.TargetPosition(state, j, rewindTicks);
                if (Vec2.SqrDistance(position, origin) > reach * reach) continue;
                EffectSystem.ApplyAbility(state, defs, events, in ctx, j);
            }
            BoxSystem.DamageCircle(state, defs, events, in ctx, origin, ability.Range);
        }
    }
}
