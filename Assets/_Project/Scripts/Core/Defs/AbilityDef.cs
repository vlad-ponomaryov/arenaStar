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

        /// <summary>Damage to every target after the first one of a piercing projectile is reduced by this percent (Nova's
        /// cone: 50 = the second target takes half); 0 = full damage to all.</summary>
        public int PierceFalloffPercent;

        /// <summary>The projectile passes through every target on its way (Chomp's log); <see cref="PierceCount"/> is ignored.</summary>
        public bool PierceAll;

        /// <summary>After a hit (without pierce left) the projectile jumps to the nearest not-yet-hit fighter some effect may
        /// affect, visible to the shooter's team, within <see cref="BounceRange"/> of the hit point — up to this many times
        /// (Kora's cup: 2 = three targets in all). Each target gets the effects of its filter (damage enemies, heal allies).</summary>
        public int BounceCount;

        /// <summary>Search radius of a bounce from the hit point (centre distance) and the flight distance after it, cells.</summary>
        public float BounceRange;

        /// <summary>Full cone angle in degrees (<see cref="DeliveryKind.MeleeCone"/>), e.g. 90.</summary>
        public float ConeAngleDegrees;

        /// <summary>Shots per activation (Pix: 4 drops); one charge pays for the whole burst. Only the attack may burst.</summary>
        public int BurstCount = 1;

        /// <summary>Ticks between shots of a burst.</summary>
        public int BurstIntervalTicks;

        /// <summary>Ticks after the last shot of an attack before the next press is accepted (a heavy swing that cannot dump
        /// all stored charges at once, D-149); 0 = none. Only the main attack uses it.</summary>
        public int RecoveryTicks;

        /// <summary>Dash length in cells (<see cref="DeliveryKind.Dash"/>).</summary>
        public float DashDistance;

        /// <summary>Dash speed, cells per second; for a <see cref="DeliveryKind.Hook"/> — the speed of the pull.</summary>
        public float DashSpeed;

        /// <summary>Breaks breakable walls on its way (Bruno's ram; walls arrive in S2.5).</summary>
        public bool BreaksWalls;

        /// <summary>Passes over walls (cones; for bots: no clear line needed — set on lobs).</summary>
        public bool IgnoresWalls;

        /// <summary>Direction = the movement input (else the facing) instead of the aim (rolls, dashes of tricks).</summary>
        public bool UsesMoveDirection;

        /// <summary>Radius of the area of a <see cref="DeliveryKind.Lob"/> / <see cref="DeliveryKind.PlaceArea"/>, cells
        /// (× the caster's <see cref="TalentModifier.AreaRadiusMul"/> talent at creation).</summary>
        public float AreaRadius;

        /// <summary>Ticks from the activation to the area becoming live: the flight of a lob (≥ the 0.5 s telegraph, GDD §5.3),
        /// the arming time of a placed area.</summary>
        public int AreaDelayTicks;

        /// <summary>Lifetime of a placed <see cref="AreaKind.Mine"/> / <see cref="AreaKind.Field"/> after it becomes live, ticks.</summary>
        public int AreaDurationTicks;

        /// <summary>Ticks between the effect pulses of a <see cref="AreaKind.Field"/> (first pulse one interval after it is live).</summary>
        public int AreaPulseTicks;

        /// <summary>Kind of the area of a <see cref="DeliveryKind.PlaceArea"/> (a lob is always a <see cref="AreaKind.Burst"/>).</summary>
        public AreaKind PlacedArea;

        /// <summary>A <see cref="AreaKind.Field"/> that stops enemy projectiles coming from outside at its edge (Kora's dome).</summary>
        public bool BlocksEnemyProjectiles;

        /// <summary>The area is visible to the owner's team always and to enemies only from closer than the bush reveal
        /// distance (Chomp's mine, D-030).</summary>
        public bool HiddenFromEnemies;

        public EffectDef[] Effects;

        public void Validate(string owner)
        {
            switch (Delivery)
            {
                case DeliveryKind.Hook:
                    if (ProjectileCount != 1 || PierceCount != 0 || PierceAll || BounceCount != 0)
                        throw new ArgumentException(owner + ": a hook is one projectile without pierce or bounce");
                    if (DashSpeed <= 0f) throw new ArgumentException(owner + ": a hook needs DashSpeed (pull speed) > 0");
                    goto case DeliveryKind.Projectile;
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
                case DeliveryKind.Lob:
                    RequireRange(owner);
                    RequireAreaRadius(owner);
                    break;
                case DeliveryKind.PlaceArea:
                    RequireAreaRadius(owner);
                    switch (PlacedArea)
                    {
                        case AreaKind.Burst:
                            break;
                        case AreaKind.Mine:
                            if (AreaDurationTicks <= 0) throw new ArgumentException(owner + ": a mine needs AreaDurationTicks > 0");
                            break;
                        case AreaKind.Field:
                            if (AreaDurationTicks <= 0) throw new ArgumentException(owner + ": a field needs AreaDurationTicks > 0");
                            if (AreaPulseTicks <= 0 || AreaPulseTicks > AreaDurationTicks)
                                throw new ArgumentException(owner + ": a field needs AreaPulseTicks in [1, AreaDurationTicks]");
                            break;
                        default:
                            throw new ArgumentException(owner + ": unsupported area kind " + PlacedArea);
                    }
                    break;
                default:
                    throw new ArgumentException(owner + ": unsupported delivery " + Delivery);
            }
            if (Range < 0f) throw new ArgumentException(owner + ": Range must be ≥ 0");
            if (PierceCount < 0) throw new ArgumentException(owner + ": PierceCount must be ≥ 0");
            if (BounceCount < 0) throw new ArgumentException(owner + ": BounceCount must be ≥ 0");
            if (PierceFalloffPercent < 0 || PierceFalloffPercent >= 100) throw new ArgumentException(owner + ": PierceFalloffPercent must be in [0, 100)");
            if (BounceCount > 0 && (Delivery != DeliveryKind.Projectile || BounceRange <= 0f))
                throw new ArgumentException(owner + ": a bounce needs a projectile and BounceRange > 0");
            if (AreaDelayTicks < 0 || AreaDurationTicks < 0 || AreaPulseTicks < 0)
                throw new ArgumentException(owner + ": area ticks must be ≥ 0");
            if (BlocksEnemyProjectiles && (Delivery != DeliveryKind.PlaceArea || PlacedArea != AreaKind.Field))
                throw new ArgumentException(owner + ": only a placed field may block projectiles");
            if (BurstCount < 1) throw new ArgumentException(owner + ": BurstCount must be ≥ 1");
            if (BurstCount > 1 && BurstIntervalTicks < 1) throw new ArgumentException(owner + ": BurstIntervalTicks must be ≥ 1");
            if (RecoveryTicks < 0) throw new ArgumentException(owner + ": RecoveryTicks must be ≥ 0");

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

        private void RequireAreaRadius(string owner)
        {
            if (AreaRadius <= 0f) throw new ArgumentException(owner + ": AreaRadius must be > 0");
        }

        /// <summary>Kind of the area this ability creates (lob: burst; placed area: <see cref="PlacedArea"/>).</summary>
        public AreaKind CreatedAreaKind => Delivery == DeliveryKind.PlaceArea ? PlacedArea : AreaKind.Burst;
    }
}
