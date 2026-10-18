using System;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.State;

namespace Arena.Bots.Utility
{
    /// <summary>
    /// When a bot uses its charged ult (GDD §7): it is worth it if <see cref="BotBehaviourDef.UltMinEnemies"/>+ perceived
    /// enemies are in its zone along the direction to the target, or the ult alone finishes the target. Then it is used with
    /// <see cref="BotDifficultyDef.UltUseChance"/> per decision; otherwise, with the target in the zone, it is wasted with
    /// <see cref="BotDifficultyDef.UltMisuseChance"/> ("rarely and off the mark"). Zones by delivery: a fan / single projectile
    /// (range and angle, the body counts), a dash (a strip of the caster's width), a cone, a circle around the caster, a lob
    /// (the blast circle at the target — any enemy that close to the target, within the throw range), a placed area (its
    /// circle around the caster), a hook (a single projectile line). Engage ults (<see cref="IsEngageUlt"/>) are worth it with the
    /// target in reach.
    /// Pure functions.
    /// </summary>
    public static class UltDecision
    {
        /// <summary>True if a fighter at <paramref name="targetPos"/> (body radius <paramref name="targetRadius"/>) is in the zone.</summary>
        public static bool IsInZone(AbilityDef ult, Vec2 origin, Vec2 dir, float casterRadius, Vec2 targetPos, float targetRadius)
        {
            Vec2 to = targetPos - origin;
            float distance = to.Length;
            switch (ult.Delivery)
            {
                case DeliveryKind.Projectile:
                case DeliveryKind.Hook:
                {
                    if (distance > ult.Range + targetRadius) return false;
                    if (distance <= targetRadius) return true;
                    float half = ult.Delivery == DeliveryKind.Projectile && ult.ProjectileCount > 1 ? ult.SpreadDegrees * 0.5f : 0f;
                    float tolerance = MathF.Atan2(targetRadius + ult.ProjectileRadius, distance) * MathUtil.RadToDeg;
                    return MathUtil.AngleBetween(dir, to) <= half + tolerance;
                }
                case DeliveryKind.Dash:
                {
                    float along = Vec2.Dot(to, dir);
                    if (along < -targetRadius || along > ult.DashDistance + targetRadius) return false;
                    return MathUtil.Abs(Vec2.Cross(dir, to)) <= casterRadius + targetRadius;
                }
                case DeliveryKind.MeleeCone:
                    if (distance > ult.Range + targetRadius) return false;
                    return distance <= targetRadius || MathUtil.AngleBetween(dir, to) <= ult.ConeAngleDegrees * 0.5f;
                case DeliveryKind.SelfArea:
                    return distance <= ult.Range + targetRadius;
                case DeliveryKind.Lob:
                    return distance <= ult.Range + ult.AreaRadius + targetRadius;
                case DeliveryKind.PlaceArea:
                    return distance <= ult.AreaRadius + targetRadius;
                default:
                    return false; // self buffs have no zone
            }
        }

        /// <summary>Perceived enemies (bits of <paramref name="perceivedMask"/>) in the zone of the ult of <paramref name="slot"/> along <paramref name="dir"/>.</summary>
        public static int CountInZone(MatchState state, MatchDefs defs, int slot, AbilityDef ult, Vec2 dir, uint perceivedMask)
        {
            ref FighterState f = ref state.Fighters[slot];
            float casterRadius = defs.Fighters[f.DefIndex].Radius;
            int count = state.Globals.FighterCount;
            int n = 0;
            for (int j = 0; j < count; j++)
            {
                if ((perceivedMask & (1u << j)) == 0) continue;
                ref FighterState t = ref state.Fighters[j];
                if (IsInZone(ult, f.Position, dir, casterRadius, t.Position, defs.Fighters[t.DefIndex].Radius)) n++;
            }
            return n;
        }

        /// <summary>
        /// Damage the ult deals to one target at <paramref name="distance"/> if aimed at it: Σ enemy damage effects
        /// (level-scaled) × the drops of a fan that can hit a body of <paramref name="targetRadius"/> at that distance.
        /// </summary>
        public static int EstimateDamageOnTarget(AbilityDef ult, int level, CombatRules rules, float distance, float targetRadius)
        {
            if (ult == null || ult.Effects == null) return 0;
            int perHit = 0;
            for (int i = 0; i < ult.Effects.Length; i++)
            {
                EffectDef e = ult.Effects[i];
                if (e.Kind == EffectKind.Damage && (e.Filter & TargetFilter.Enemy) != 0) perHit += rules.ScaleByLevel(e.Value, level);
            }
            if (ult.Delivery != DeliveryKind.Projectile || ult.ProjectileCount <= 1) return perHit;

            int drops = ult.ProjectileCount;
            float step = ult.SpreadDegrees / (drops - 1);
            if (step <= 0f) return perHit * drops;
            float d = MathUtil.Max(distance, MathUtil.DistanceEpsilon);
            float covered = 2f * MathF.Atan2(targetRadius + ult.ProjectileRadius, d) * MathUtil.RadToDeg;
            int hits = 1 + (int)MathF.Floor(covered / step);
            return perHit * MathUtil.Min(hits, drops);
        }

        /// <summary>
        /// A single-target "engage" ult — a hook (Vex pulls himself in) or a self buff (Nova's eye: reveal + boosted shots) —
        /// is worth using whenever the target is in its reach (the hook's line, the attack range for a buff).
        /// </summary>
        public static bool IsEngageUlt(AbilityDef ult) =>
            ult != null && (ult.Delivery == DeliveryKind.Hook || ult.Delivery == DeliveryKind.SelfBuff);

        /// <summary>The ult is worth using: enough enemies in the zone, or it finishes the target.</summary>
        public static bool IsWorthIt(int enemiesInZone, int targetHealth, int damageOnTarget, BotBehaviourDef b) =>
            enemiesInZone >= b.UltMinEnemies || (damageOnTarget > 0 && targetHealth <= damageOnTarget);

        /// <summary>The per-decision roll of the difficulty: use when worth it, waste it on a target in the zone otherwise.</summary>
        public static bool Roll(bool worthIt, bool targetInZone, BotDifficultyDef d, ref DeterministicRng rng)
        {
            if (worthIt) return rng.NextFloat01() < d.UltUseChance;
            return targetInZone && d.UltMisuseChance > 0f && rng.NextFloat01() < d.UltMisuseChance;
        }
    }
}
