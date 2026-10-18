using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Hand-built defs with the full sets of Chomp and Kora (GDD §6 values, independent of the seeded assets):
    /// fighter 0 = Chomp, 1 = Kora, 2 = sturdy dummy without abilities, 3 = straight shooter (Pix-like burst of
    /// <see cref="TestMatchFactory.DropDamage"/>). Rules are those of <see cref="TestMatchFactory"/>.
    /// </summary>
    public static class SupportFighterTestFactory
    {
        public const int Chomp = 0;
        public const int Kora = 1;
        public const int Dummy = 2;
        public const int Shooter = 3;

        public const float ChompRadius = 0.45f;
        public const float LobRange = 7f;
        public const float BlastRadius = 1.2f;
        public const int LobFlightTicks = 15;
        public const int BombDamage = 1100;
        public const int ChompUltCharge = 6600;
        public const float LogRange = 9f;
        public const float LogSpeed = 10f;
        public const float LogRadius = 0.6f;
        public const int LogDamage = 1600;
        public const float LogPush = 2f;
        public const int LogPushTicks = 6;
        public const float MineRadius = 1f;
        public const int MineTicks = 300;
        public const int MineDamage = 900;
        public const int ChompTalentPercent = 20;

        public const int KoraHealth = 4200;
        public const float KoraRadius = 0.5f;
        public const float CupRange = 7f;
        public const float CupSpeed = 10f;
        public const float CupRadius = 0.25f;
        public const int Bounces = 2;
        public const float BounceRange = 3f;
        public const int CupDamage = 600;
        public const int CupHeal = 500;
        public const int KoraUltCharge = 4800;
        public const float DomeRadius = 2.5f;
        public const int DomeTicks = 90;
        public const int DomePulseTicks = 15;
        public const int DomeHeal = 1000;
        public const float BrewRadius = 3f;
        public const int BrewHeal = 1200;
        public const int AuraRadius = 3;
        public const int AuraRegenTicks = 60;

        public static MatchDefs CreateDefs() => WithMap(TestMatchFactory.CreateDefs());

        /// <summary>Test defs on a map from text rows (top row first, legend of <c>MapConverter</c>).</summary>
        public static MatchDefs CreateDefsOnMap(params string[] rows) => WithMap(TestMatchFactory.CreateDefsOnMap(rows));

        private static MatchDefs WithMap(MatchDefs defs)
        {
            FighterDef shooter = defs.Fighters[0];
            defs.Fighters = new[] { CreateChomp(), CreateKora(), AbilityTestFactory.CreateDummy(), shooter };
            return defs;
        }

        public static FighterDef CreateChomp() => new FighterDef
        {
            Id = "chomp", NameKey = "fighter.chomp.name", MaxHealth = 3000, MoveSpeed = TestMatchFactory.Speed, Radius = ChompRadius,
            AttackRechargeTicks = 48,
            Attack = new AbilityDef
            {
                Delivery = DeliveryKind.Lob, Range = LobRange, AreaRadius = BlastRadius, AreaDelayTicks = LobFlightTicks,
                IgnoresWalls = true, Effects = TestMatchFactory.EnemyDamage(BombDamage)
            },
            UltChargeRequired = ChompUltCharge,
            Ult = new AbilityDef
            {
                Delivery = DeliveryKind.Projectile, Range = LogRange, ProjectileSpeed = LogSpeed, ProjectileRadius = LogRadius,
                PierceAll = true, BreaksWalls = true,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = LogDamage },
                    new EffectDef { Kind = EffectKind.Knockback, Filter = TargetFilter.Enemy, Distance = LogPush, DurationTicks = LogPushTicks }
                }
            },
            Trick = new AbilityDef
            {
                Delivery = DeliveryKind.PlaceArea, PlacedArea = AreaKind.Mine, AreaRadius = MineRadius, AreaDurationTicks = MineTicks,
                HiddenFromEnemies = true, Effects = TestMatchFactory.EnemyDamage(MineDamage)
            },
            Talent = new TalentDef { Condition = TalentCondition.Always, Modifier = TalentModifier.AreaRadiusMul, Value = ChompTalentPercent }
        };

        public static FighterDef CreateKora() => new FighterDef
        {
            Id = "kora", NameKey = "fighter.kora.name", MaxHealth = KoraHealth, MoveSpeed = 2.2f, Radius = KoraRadius,
            AttackRechargeTicks = 45,
            Attack = new AbilityDef
            {
                Delivery = DeliveryKind.Projectile, Range = CupRange, ProjectileSpeed = CupSpeed, ProjectileRadius = CupRadius,
                BounceCount = Bounces, BounceRange = BounceRange,
                Effects = new[]
                {
                    new EffectDef { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = CupDamage },
                    new EffectDef { Kind = EffectKind.Heal, Filter = TargetFilter.Ally, Value = CupHeal }
                }
            },
            UltChargeRequired = KoraUltCharge,
            Ult = new AbilityDef
            {
                Delivery = DeliveryKind.PlaceArea, PlacedArea = AreaKind.Field, AreaRadius = DomeRadius, AreaDurationTicks = DomeTicks,
                AreaPulseTicks = DomePulseTicks, BlocksEnemyProjectiles = true,
                Effects = new[] { new EffectDef { Kind = EffectKind.Heal, Filter = TargetFilter.Ally | TargetFilter.Self, Value = DomeHeal } }
            },
            Trick = new AbilityDef
            {
                Delivery = DeliveryKind.SelfArea, Range = BrewRadius,
                Effects = new[] { new EffectDef { Kind = EffectKind.Heal, Filter = TargetFilter.Ally | TargetFilter.Self, Value = BrewHeal } }
            },
            Talent = new TalentDef
            {
                Condition = TalentCondition.AllyAura, Param = AuraRadius, Modifier = TalentModifier.RegenDelay, Ticks = AuraRegenTicks
            }
        };

        public static MatchSimulation Create(params PlayerSlotSetup[] slots) => TestMatchFactory.CreateWith(CreateDefs(), slots);

        public static MatchSimulation Create(bool unlockAll, MatchDefs defs, params PlayerSlotSetup[] slots) =>
            new MatchSimulation(defs, new MatchSetup { Seed = 1, Slots = slots, UnlockAllAbilities = unlockAll });

        public static PlayerSlotSetup Slot(int fighter, int team, float x, float y) => new PlayerSlotSetup(fighter, team, 1, new Vec2(x, y));

        /// <summary>Sets health and freezes regeneration of <paramref name="slot"/> (no regen ticks in heal checks).</summary>
        public static void Wound(MatchSimulation sim, int slot, int health)
        {
            ref FighterState f = ref sim.State.Fighters[slot];
            f.Health = health;
            f.LastCombatTick = FrozenRegenTick;
        }

        /// <summary>A "last combat" tick far in the future: regeneration does not start during a test.</summary>
        public const int FrozenRegenTick = 1000000;
    }
}
