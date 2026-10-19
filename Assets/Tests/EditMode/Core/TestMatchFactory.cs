using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.Sim;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>Hand-built defs and setups for Core tests (independent of the seeded assets).</summary>
    public static class TestMatchFactory
    {
        public const int MaxHealth = 3400;
        public const float Speed = 2.5f;
        public const float Radius = 0.5f;
        public const int RechargeTicks = 42;
        public const int RegenDelayTicks = 90;
        public const float RegenFraction = 0.12f;
        public const float ArenaWidth = 21f;
        public const float ArenaHeight = 31f;
        public const int RespawnTicks = 90;
        public const float UltLossOnDeath = 0.5f;
        public const int TrickUses = 3;
        public const int TrickCooldownTicks = 150;
        public const int TrickUnlockLevel = 5;
        public const int TalentUnlockLevel = 8;
        public const float BushRevealDistance = 2f;
        public const int RevealTicks = 30;

        // Fighter 0 ("test", Pix-like): burst of straight drops.
        public const float ShooterRange = 8f;
        public const float DropSpeed = 12f;
        public const float DropRadius = 0.2f;
        public const int BurstCount = 4;
        public const int BurstIntervalTicks = 3;
        public const int DropDamage = 380;

        // Fighter 1 ("test2", Bruno-like): instant 90° cone.
        public const float ConeRange = 2.5f;
        public const float ConeAngle = 90f;
        public const int ConeDamage = 1200;
        public const float TankRadius = 0.55f;
        public const int TankHealth = 5600;

        public static AbilityDef ShooterAttack() => new AbilityDef
        {
            Delivery = DeliveryKind.Projectile, Range = ShooterRange, ProjectileSpeed = DropSpeed, ProjectileRadius = DropRadius,
            BurstCount = BurstCount, BurstIntervalTicks = BurstIntervalTicks, Effects = EnemyDamage(DropDamage)
        };

        public static AbilityDef ConeAttack() => new AbilityDef
        {
            Delivery = DeliveryKind.MeleeCone, Range = ConeRange, ConeAngleDegrees = ConeAngle, BurstCount = 1,
            Effects = EnemyDamage(ConeDamage)
        };

        public static EffectDef[] EnemyDamage(int value) =>
            new[] { new EffectDef { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = value } };

        public static MatchDefs CreateDefs(float moveSpeed = Speed, int rechargeTicks = RechargeTicks)
        {
            return new MatchDefs
            {
                Rules = new CombatRules
                {
                    MaxCharges = 3,
                    RegenDelayTicks = RegenDelayTicks,
                    RegenFractionPerSecond = RegenFraction,
                    RespawnTicks = RespawnTicks,
                    LevelBonusPerLevel = 0.06f,
                    UltLossOnDeath = UltLossOnDeath,
                    TrickUses = TrickUses,
                    TrickCooldownTicks = TrickCooldownTicks,
                    TrickUnlockLevel = TrickUnlockLevel,
                    TalentUnlockLevel = TalentUnlockLevel,
                    BushRevealDistance = BushRevealDistance,
                    RevealTicks = RevealTicks,
                    AutoAimPreferClearShot = true
                },
                Fighters = new[]
                {
                    new FighterDef
                    {
                        Id = "test", NameKey = "fighter.test.name", MaxHealth = MaxHealth, MoveSpeed = moveSpeed,
                        Radius = Radius, AttackRechargeTicks = rechargeTicks, Attack = ShooterAttack()
                    },
                    new FighterDef
                    {
                        Id = "test2", NameKey = "fighter.test2.name", MaxHealth = TankHealth, MoveSpeed = 2.2f,
                        Radius = TankRadius, AttackRechargeTicks = 33, Attack = ConeAttack()
                    }
                },
                Map = TestMap()
            };
        }

        /// <summary>All-floor 21×31 map with 3 centrally symmetric spawns per team (rows 2 and 28, columns 6/10/14).</summary>
        public static MapDef TestMap()
        {
            int w = (int)ArenaWidth, h = (int)ArenaHeight;
            var map = new MapDef { Id = "test", Width = w, Height = h, Tiles = new TileType[w * h] };
            map.Spawns = new[]
            {
                new MapSpawn(0, new GridCell(6, 2)), new MapSpawn(0, new GridCell(10, 2)), new MapSpawn(0, new GridCell(14, 2)),
                new MapSpawn(1, new GridCell(14, 28)), new MapSpawn(1, new GridCell(10, 28)), new MapSpawn(1, new GridCell(6, 28))
            };
            map.Validate();
            return map;
        }

        /// <summary>Test defs on a map from text rows (top row first, legend of <c>MapConverter</c>).</summary>
        public static MatchDefs CreateDefsOnMap(params string[] rows)
        {
            MatchDefs defs = CreateDefs();
            defs.Map = Arena.Data.Conversion.MapConverter.FromRows("test_rows", rows);
            return defs;
        }

        /// <summary>Slots alternate teams 0/1 and fighters 0/1; spawns are spread around the arena.</summary>
        public static MatchSetup CreateSetup(int slots = 1, int seed = 1, int level = 1)
        {
            var setup = new MatchSetup { Seed = seed, Slots = new PlayerSlotSetup[slots] };
            for (int i = 0; i < slots; i++)
            {
                int team = i % 2;
                var spawn = new Vec2(3f + 3f * (i / 2), team == 0 ? 4f : ArenaHeight - 4f);
                setup.Slots[i] = new PlayerSlotSetup(i % 2, team, level, spawn);
            }
            return setup;
        }

        /// <summary>One fighter of def 0 at the arena centre.</summary>
        public static MatchSimulation CreateSingle(float moveSpeed = Speed)
        {
            var setup = new MatchSetup
            {
                Seed = 1,
                Slots = new[] { new PlayerSlotSetup(0, 0, 1, new Vec2(ArenaWidth * 0.5f, ArenaHeight * 0.5f)) }
            };
            return new MatchSimulation(CreateDefs(moveSpeed), setup);
        }

        /// <summary>Simulation with explicit slots (fighter index, team, spawn).</summary>
        public static MatchSimulation CreateWith(MatchDefs defs, params PlayerSlotSetup[] slots) =>
            new MatchSimulation(defs, new MatchSetup { Seed = 1, Slots = slots });

        public static PlayerSlotSetup Slot(int fighter, int team, float x, float y) => new PlayerSlotSetup(fighter, team, 1, new Vec2(x, y));

        public static PlayerInput[] Inputs() => new PlayerInput[SimLimits.MaxFighters];

        public static void Run(MatchSimulation sim, PlayerInput[] inputs, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Tick(inputs);
        }

        /// <summary>Pseudo-random but reproducible inputs (movement, aim, occasional buttons).</summary>
        public static void FillRandomInputs(ref DeterministicRng rng, PlayerInput[] inputs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var move = new Vec2(rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                var aim = rng.NextFloat01() < 0.5f ? Vec2.Zero : new Vec2(rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                var buttons = (InputButtons)(rng.Range(0, 16));
                inputs[i] = new PlayerInput(move, aim, buttons);
            }
        }
    }
}
