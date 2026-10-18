using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Data of one ability (D-011): delivery + its parameters + list of effects. Attack, ult and trick share this type and
    /// the same systems; a new fighter is new data, not new code. Values are in cells / ticks; immutable during the match.
    /// </summary>
    public sealed class AbilityDef
    {
        public DeliveryKind Delivery;

        /// <summary>Reach in cells: projectile flight distance or cone radius. For the main attack also the auto-aim radius.</summary>
        public float Range;

        /// <summary>Projectile speed, cells per second (<see cref="DeliveryKind.Projectile"/>).</summary>
        public float ProjectileSpeed;

        /// <summary>Projectile hit radius in cells (added to the target's body radius).</summary>
        public float ProjectileRadius;

        /// <summary>Extra targets a projectile passes through after the first hit (0 = stops at the first target).</summary>
        public int PierceCount;

        /// <summary>Full cone angle in degrees (<see cref="DeliveryKind.MeleeCone"/>), e.g. 90.</summary>
        public float ConeAngleDegrees;

        /// <summary>Shots per activation (Pix: 4 drops); one charge pays for the whole burst.</summary>
        public int BurstCount;

        /// <summary>Ticks between shots of a burst.</summary>
        public int BurstIntervalTicks;

        public EffectDef[] Effects;

        public void Validate(string owner)
        {
            switch (Delivery)
            {
                case DeliveryKind.Projectile:
                    if (ProjectileSpeed <= 0f) throw new ArgumentException(owner + ": ProjectileSpeed must be > 0");
                    if (ProjectileRadius < 0f) throw new ArgumentException(owner + ": ProjectileRadius must be ≥ 0");
                    break;
                case DeliveryKind.MeleeCone:
                    if (ConeAngleDegrees <= 0f || ConeAngleDegrees > 360f)
                        throw new ArgumentException(owner + ": ConeAngleDegrees must be in (0, 360]");
                    break;
                default:
                    throw new ArgumentException(owner + ": unsupported delivery " + Delivery);
            }
            if (Range <= 0f) throw new ArgumentException(owner + ": Range must be > 0");
            if (PierceCount < 0) throw new ArgumentException(owner + ": PierceCount must be ≥ 0");
            if (BurstCount < 1) throw new ArgumentException(owner + ": BurstCount must be ≥ 1");
            if (BurstCount > 1 && BurstIntervalTicks < 1) throw new ArgumentException(owner + ": BurstIntervalTicks must be ≥ 1");
            if (Effects == null || Effects.Length == 0) throw new ArgumentException(owner + ": ability has no effects");
            for (int i = 0; i < Effects.Length; i++)
            {
                if (Effects[i] == null) throw new ArgumentException(owner + ": effect " + i + " is null");
                Effects[i].Validate(owner);
            }
        }

        /// <summary>True if any effect may affect a target with this relation (enemy / ally / self).</summary>
        public bool Affects(TargetFilter relation)
        {
            for (int i = 0; i < Effects.Length; i++)
                if ((Effects[i].Filter & relation) != 0) return true;
            return false;
        }
    }
}
