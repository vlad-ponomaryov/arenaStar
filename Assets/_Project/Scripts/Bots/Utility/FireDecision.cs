using Arena.Core.Defs;

namespace Arena.Bots.Utility
{
    /// <summary>
    /// Charge economy of a bot (GDD §7: "does not waste the last charge"): with two or more charges it shoots freely; the
    /// last one only if one attack finishes the target or the enemy is dangerously close.
    /// </summary>
    public static class FireDecision
    {
        /// <summary>
        /// Damage of one full attack activation on an enemy if everything hits: Σ enemy damage effects (level-scaled) ×
        /// projectiles per shot × burst shots. Ignores damage reduction and talents (an estimate).
        /// </summary>
        public static int EstimateAttackDamage(FighterDef def, int level, CombatRules rules)
        {
            AbilityDef attack = def.Attack;
            if (attack == null || attack.Effects == null) return 0;
            int perHit = 0;
            for (int i = 0; i < attack.Effects.Length; i++)
            {
                EffectDef e = attack.Effects[i];
                if (e.Kind == EffectKind.Damage && (e.Filter & TargetFilter.Enemy) != 0) perHit += rules.ScaleByLevel(e.Value, level);
            }
            int perShot = attack.Delivery == DeliveryKind.Projectile ? perHit * attack.ProjectileCount : perHit;
            return perShot * attack.BurstCount;
        }

        /// <summary>True if spending a charge now is sensible (see the class summary).</summary>
        public static bool ShouldSpendCharge(int charges, int targetHealth, int attackDamage, float targetDistance, float lastChargeDistance)
        {
            if (charges <= 0) return false;
            if (charges >= 2) return true;
            return targetHealth <= attackDamage || targetDistance < lastChargeDistance;
        }
    }
}
