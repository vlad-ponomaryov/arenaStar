using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Hand-built defs with full ability sets (independent of the seeded assets): fighter 0 = Pix-like, 1 = Bruno-like
    /// (GDD §6 values), 2 = a sturdy dummy without abilities. Rules are those of <see cref="TestMatchFactory"/>.
    /// </summary>
    public static class AbilityTestFactory
    {
        public const int Pix = 0;
        public const int Bruno = 1;
        public const int Dummy = 2;

        public const int PixUltCharge = 7600;
        public const int PixUltDrops = 10;
        public const float PixUltSpread = 50f;
        public const int PixUltDamage = 400;
        public const float PixRollDistance = 3f;
        public const float PixRollSpeed = 15f;
        public const int PixRollTicks = 6;
        public const int PixTalentSpeedPercent = 10;

        public const int BrunoHealth = 5600;
        public const float BrunoRadius = 0.55f;
        public const int BrunoUltCharge = 7200;
        public const float RamDistance = 6f;
        public const float RamSpeed = 15f;
        public const int RamTicks = 12;
        public const int RamDamage = 1000;
        public const float RamPush = 2f;
        public const int RamPushTicks = 6;
        public const int RamStunTicks = 15;
        public const int HelmetPercent = 40;
        public const int HelmetTicks = 60;
        public const int BrunoTalentHealthPercent = 40;
        public const int BrunoTalentDamagePercent = 15;

        public const int DummyHealth = 50000;
        public const float DummyRadius = 0.5f;

        public static MatchDefs CreateDefs()
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            defs.Fighters = new[] { CreatePix(), CreateBruno(), CreateDummy() };
            return defs;
        }

        public static FighterDef CreatePix() => new FighterDef
        {
            Id = "pix", NameKey = "fighter.pix.name", MaxHealth = TestMatchFactory.MaxHealth, MoveSpeed = TestMatchFactory.Speed,
            Radius = 0.45f, AttackRechargeTicks = TestMatchFactory.RechargeTicks, Attack = TestMatchFactory.ShooterAttack(),
            UltChargeRequired = PixUltCharge,
            Ult = new AbilityDef
            {
                Delivery = DeliveryKind.Projectile, Range = TestMatchFactory.ShooterRange, ProjectileSpeed = TestMatchFactory.DropSpeed,
                ProjectileRadius = TestMatchFactory.DropRadius, ProjectileCount = PixUltDrops, SpreadDegrees = PixUltSpread,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = PixUltDamage },
                    new EffectDef { Kind = EffectKind.Knockback, Filter = TargetFilter.Enemy, Distance = 0.5f, DurationTicks = 3 }
                }
            },
            Trick = new AbilityDef
            {
                Delivery = DeliveryKind.Dash, DashDistance = PixRollDistance, DashSpeed = PixRollSpeed, UsesMoveDirection = true,
                Effects = new EffectDef[0]
            },
            Talent = new TalentDef { Condition = TalentCondition.UltCharged, Modifier = TalentModifier.MoveSpeedMul, Value = PixTalentSpeedPercent }
        };

        public static FighterDef CreateBruno() => new FighterDef
        {
            Id = "bruno", NameKey = "fighter.bruno.name", MaxHealth = BrunoHealth, MoveSpeed = TestMatchFactory.Speed,
            Radius = BrunoRadius, AttackRechargeTicks = 33, Attack = TestMatchFactory.ConeAttack(),
            UltChargeRequired = BrunoUltCharge,
            Ult = new AbilityDef
            {
                Delivery = DeliveryKind.Dash, Range = RamDistance, DashDistance = RamDistance, DashSpeed = RamSpeed, BreaksWalls = true,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = RamDamage },
                    new EffectDef { Kind = EffectKind.Knockback, Filter = TargetFilter.Enemy, Distance = RamPush, DurationTicks = RamPushTicks },
                    new EffectDef { Kind = EffectKind.Stun, Filter = TargetFilter.Enemy, DurationTicks = RamStunTicks }
                }
            },
            Trick = new AbilityDef
            {
                Delivery = DeliveryKind.SelfBuff,
                Effects = new[] { new EffectDef { Kind = EffectKind.DamageReduction, Filter = TargetFilter.Self, Value = HelmetPercent, DurationTicks = HelmetTicks } }
            },
            Talent = new TalentDef
            {
                Condition = TalentCondition.HealthBelow, Param = BrunoTalentHealthPercent,
                Modifier = TalentModifier.DamageMul, Value = BrunoTalentDamagePercent
            }
        };

        public static FighterDef CreateDummy() => new FighterDef
        {
            Id = "dummy", NameKey = "fighter.dummy.name", MaxHealth = DummyHealth, MoveSpeed = TestMatchFactory.Speed,
            Radius = DummyRadius, AttackRechargeTicks = 30, Attack = TestMatchFactory.ConeAttack()
        };

        public static MatchSimulation Create(params PlayerSlotSetup[] slots) => TestMatchFactory.CreateWith(CreateDefs(), slots);

        /// <summary>Simulation with an explicit unlock flag (D-012) and levels in the slots.</summary>
        public static MatchSimulation Create(bool unlockAll, params PlayerSlotSetup[] slots) =>
            new MatchSimulation(CreateDefs(), new MatchSetup { Seed = 1, Slots = slots, UnlockAllAbilities = unlockAll });

        public static PlayerSlotSetup Slot(int fighter, int team, float x, float y, int level = 1) =>
            new PlayerSlotSetup(fighter, team, level, new Vec2(x, y));

        /// <summary>One tick with <paramref name="buttons"/> pressed by <paramref name="slot"/>, then the input is cleared.</summary>
        public static void Press(MatchSimulation sim, PlayerInput[] inputs, int slot, InputButtons buttons, Vec2 aim = default, Vec2 move = default)
        {
            inputs[slot] = new PlayerInput(move, aim, buttons);
            sim.Tick(inputs);
            inputs[slot] = default;
        }

        /// <summary>Fills the ult of <paramref name="slot"/> to its (level-scaled) requirement.</summary>
        public static void ChargeUlt(MatchSimulation sim, int slot)
        {
            ref var f = ref sim.State.Fighters[slot];
            f.UltCharge = Arena.Core.Sim.Systems.UltSystem.Required(sim.Defs, in f);
        }
    }
}
