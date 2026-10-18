using System;

namespace Arena.Core.Defs
{
    /// <summary>
    /// Data of one ability (D-011): delivery + its parameters + list of effects. Attack, ult and trick share this type and
    /// the same systems (<c>AbilitySystem</c>); a new fighter is new data, not new code. Values are in cells / ticks;
    /// immutable during the match. Parameters of a delivery the ability does not use are ignored.
    /// </summary>
    public sealed class AbilityDef
    {
        public DeliveryKind Delivery;

        /// <summary>Reach in cells: projectile flight distance, cone radius, self-area radius. Also the auto-aim radius
        /// of this ability (for the main attack — the attack range, D-007).</summary>
        public float Range;

        /// <summary>Projectile speed, cells per second (<see cref="DeliveryKind.Projectile"/>).</summary>
        public float ProjectileSpeed;

        /// <summary>Projectile hit radius in cells (added to the target body radius).</summary>
        public float ProjectileRadius;

        /// <summary>Projectiles per shot: 1 = single, more = a fan spread evenly over <see cref="SpreadDegrees"/>.</summary>
        public int ProjectileCount = 1;

        /// <summary>Full angle of a fan between the first and the last projectile, degrees.</summary>
        public float SpreadDegrees;

        /// <summary>Extra targets a projectile passes through after the first hit (0 = stops at the first target).</summary>
        public int PierceCount;

        /// <summary>Full cone angle in degrees (<see cref="DeliveryKind.MeleeCone"/>), e.g. 90.</summary>
        public float ConeAngleDegrees;

        /// <summary>Shots per activation (Pix: 4 drops); one charge pays for the whole burst. Only the attack may burst.</summary>
        public int BurstCount = 1;

        /// <summary>Ticks between shots of a burst.</summary>
        public int BurstIntervalTicks;

        /// <summary>Dash length in cells (<see cref="DeliveryKind.Dash"/>).</summary>
        public float DashDistance;

        /// <summary>Dash speed, cells per second.</summary>
        public float DashSpeed;

        /// <summary>Breaks breakable walls on its way (Bruno's ram; walls arrive in S2.5).</summary>
        public bool BreaksWalls;

        /// <summary>Passes over walls (lobbed bombs; walls arrive in S2.5).</summary>
        public bool IgnoresWalls;

        /// <summary>Direction = the movement input (else the facing) instead of the aim (rolls, dashes of tricks).</summary>
        public bool UsesMoveDirection;

        public EffectDef[] Effects;

        public void Validate(string owner)
        {
            switch (Delivery)
            {
                case DeliveryKind.Projectile:
                    if (ProjectileSpeed <= 0f) throw new ArgumentException(owner + ": ProjectileSpeed must be > 0");
                    if (ProjectileRadius < 0f) throw new ArgumentException(owner + ": ProjectileRadius must be ≥ 0");
                    if (ProjectileCount < 1) throw new ArgumentException(owner + ": ProjectileCount must be ≥ 1");
                    if (SpreadDegrees < 0f || SpreadDegrees > 360f) throw new ArgumentException(owner + ": SpreadDegrees must be in [0, 360]");
                    RequireRange(owner);
                    break;
                case DeliveryKind.MeleeCone:
                    if (ConeAngleDegrees <= 0f || ConeAngleDegrees > 360f)
                        throw new ArgumentException(owner + ": ConeAngleDegrees must be in (0, 360]");
                    RequireRange(owner);
                    break;
                case DeliveryKind.Dash:
                    if (DashDistance <= 0f) throw new ArgumentException(owner + ": DashDistance must be > 0");
                    if (DashSpeed <= 0f) throw new ArgumentException(owner + ": DashSpeed must be > 0");
                    break;
                case DeliveryKind.SelfArea:
                    RequireRange(owner);
                    break;
                case DeliveryKind.SelfBuff:
                    break;
                default:
                    throw new ArgumentException(owner + ": unsupported delivery " + Delivery);
            }
            if (Range < 0f) throw new ArgumentException(owner + ": Range must be ≥ 0");
            if (PierceCount < 0) throw new ArgumentException(owner + ": PierceCount must be ≥ 0");
            if (BurstCount < 1) throw new ArgumentException(owner + ": BurstCount must be ≥ 1");
            if (BurstCount > 1 && BurstIntervalTicks < 1) throw new ArgumentException(owner + ": BurstIntervalTicks must be ≥ 1");

            if (Effects == null) throw new ArgumentException(owner + ": Effects is null");
            // Only a dash may have no effects (a pure movement trick); everything else must do something.
            if (Effects.Length == 0 && Delivery != DeliveryKind.Dash) throw new ArgumentException(owner + ": ability has no effects");
            for (int i = 0; i < Effects.Length; i++)
            {
                if (Effects[i] == null) throw new ArgumentException(owner + ": effect " + i + " is null");
                Effects[i].Validate(owner);
            }
            if (Delivery == DeliveryKind.SelfBuff && !Affects(TargetFilter.Self))
                throw new ArgumentException(owner + ": a self buff needs effects with the Self filter");
        }

        /// <summary>True if any effect may affect a target with this relation (enemy / ally / self).</summary>
        public bool Affects(TargetFilter relation)
        {
            for (int i = 0; i < Effects.Length; i++)
                if ((Effects[i].Filter & relation) != 0) return true;
            return false;
        }

        private void RequireRange(string owner)
        {
            if (Range <= 0f) throw new ArgumentException(owner + ": Range must be > 0");
        }
    }
}
