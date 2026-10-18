using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Hand-built defs with the full sets of Nova and Vex (GDD §6 values, independent of the seeded assets): fighter 0 = Nova,
    /// 1 = Vex, 2 = sturdy dummy without abilities, 3 = straight shooter (Pix-like burst). Rules are those of
    /// <see cref="TestMatchFactory"/>.
    /// </summary>
    public static class ScoutFighterTestFactory
    {
        public const int Nova = 0;
        public const int Vex = 1;
        public const int Dummy = 2;
        public const int Shooter = 3;

        public const int NovaHealth = 2600;
        public const float NovaRadius = 0.45f;
        public const int NovaRechargeTicks = 60;
        public const float ConeRange = 10f;
        public const float ConeSpeed = 18f;
        public const float ConeRadius = 0.15f;
        public const int ConeDamage = 1500;
        public const int PierceFalloff = 50;
        public const int MarkTicks = 120;
        public const int NovaUltCharge = 9000;
        public const int RevealTicks = 150;
        public const int BoostPercent = 30;
        public const int BoostShots = 3;
        public const float FlapRadius = 2f;
        public const float FlapPush = 3f;
        public const int FlapPushTicks = 9;
        public const int NovaTalentPercent = 20;

        public const int VexHealth = 3600;
        public const float VexRadius = 0.45f;
        public const int VexRechargeTicks = 27;
        public const float ClawRange = 1.8f;
        public const int ClawDamage = 520;
        public const int ClawHits = 2;
        public const int ClawIntervalTicks = 5;
        public const float HookRange = 7f;
        public const float HookSpeed = 20f;
        public const float HookRadius = 0.3f;
        public const float PullSpeed = 20f;
        public const int HookDamage = 800;
        public const int HookSlowPercent = 30;
        public const int HookSlowTicks = 30;
        public const int VexUltCharge = 10400;
        public const int SmokeTicks = 45;

        public static MatchDefs CreateDefs() => WithFighters(TestMatchFactory.CreateDefs());

        /// <summary>Test defs on a map from text rows (top row first, legend of <c>MapConverter</c>).</summary>
        public static MatchDefs CreateDefsOnMap(params string[] rows) => WithFighters(TestMatchFactory.CreateDefsOnMap(rows));

        private static MatchDefs WithFighters(MatchDefs defs)
        {
            FighterDef shooter = defs.Fighters[0];
            defs.Fighters = new[] { CreateNova(), CreateVex(), AbilityTestFactory.CreateDummy(), shooter };
            return defs;
        }

        public static FighterDef CreateNova() => new FighterDef
        {
            Id = "nova", NameKey = "fighter.nova.name", MaxHealth = NovaHealth, MoveSpeed = TestMatchFactory.Speed, Radius = NovaRadius,
            AttackRechargeTicks = NovaRechargeTicks,
            Attack = new AbilityDef
            {
                Delivery = DeliveryKind.Projectile, Range = ConeRange, ProjectileSpeed = ConeSpeed, ProjectileRadius = ConeRadius,
                PierceCount = 1, PierceFalloffPercent = PierceFalloff,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = ConeDamage },
                    new EffectDef { Kind = EffectKind.Mark, Filter = TargetFilter.Enemy, DurationTicks = MarkTicks }
                }
            },
            UltChargeRequired = NovaUltCharge,
            Ult = new AbilityDef
            {
                Delivery = DeliveryKind.SelfBuff,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.TeamReveal, Filter = TargetFilter.Self, DurationTicks = RevealTicks },
                    new EffectDef { Kind = EffectKind.BoostShots, Filter = TargetFilter.Self, Value = BoostPercent, Count = BoostShots }
                }
            },
            Trick = new AbilityDef
            {
                Delivery = DeliveryKind.SelfArea, Range = FlapRadius,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.Knockback, Filter = TargetFilter.Enemy, Distance = FlapPush, DurationTicks = FlapPushTicks }
                }
            },
            Talent = new TalentDef { Condition = TalentCondition.TargetMarked, Modifier = TalentModifier.DamageMul, Value = NovaTalentPercent }
        };

        public static FighterDef CreateVex() => new FighterDef
        {
            Id = "vex", NameKey = "fighter.vex.name", MaxHealth = VexHealth, MoveSpeed = 2.8f, Radius = VexRadius,
            AttackRechargeTicks = VexRechargeTicks,
            Attack = new AbilityDef
            {
                Delivery = DeliveryKind.MeleeCone, Range = ClawRange, ConeAngleDegrees = 90f, BurstCount = ClawHits,
                BurstIntervalTicks = ClawIntervalTicks, Effects = TestMatchFactory.EnemyDamage(ClawDamage)
            },
            UltChargeRequired = VexUltCharge,
            Ult = new AbilityDef
            {
                Delivery = DeliveryKind.Hook, Range = HookRange, ProjectileSpeed = HookSpeed, ProjectileRadius = HookRadius,
                DashSpeed = PullSpeed,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = HookDamage },
                    new EffectDef { Kind = EffectKind.Slow, Filter = TargetFilter.Enemy, Value = HookSlowPercent, DurationTicks = HookSlowTicks }
                }
            },
            Trick = new AbilityDef
            {
                Delivery = DeliveryKind.SelfBuff,
                Effects = new[] { new EffectDef { Kind = EffectKind.Invisibility, Filter = TargetFilter.Self, DurationTicks = SmokeTicks } }
            },
            Talent = new TalentDef { Condition = TalentCondition.OnKill, Modifier = TalentModifier.RefillCharges }
        };

        public static MatchSimulation Create(params PlayerSlotSetup[] slots) => TestMatchFactory.CreateWith(CreateDefs(), slots);

        public static MatchSimulation Create(bool unlockAll, MatchDefs defs, params PlayerSlotSetup[] slots) =>
            new MatchSimulation(defs, new MatchSetup { Seed = 1, Slots = slots, UnlockAllAbilities = unlockAll });

        public static PlayerSlotSetup Slot(int fighter, int team, float x, float y) => new PlayerSlotSetup(fighter, team, 1, new Vec2(x, y));

        /// <summary>Health lost by <paramref name="slot"/> since full (dummies regenerate only after 3 s without damage).</summary>
        public static int Lost(MatchSimulation sim, int slot)
        {
            ref FighterState f = ref sim.State.Fighters[slot];
            return f.MaxHealth - f.Health;
        }
    }
}
