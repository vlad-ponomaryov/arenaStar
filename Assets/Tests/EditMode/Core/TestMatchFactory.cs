using Arena.Core.Defs;
using Arena.Core.Input;
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

        public static MatchDefs CreateDefs(float moveSpeed = Speed, int rechargeTicks = RechargeTicks)
        {
            return new MatchDefs
            {
                Rules = new CombatRules
                {
                    MaxCharges = 3,
                    RegenDelayTicks = RegenDelayTicks,
                    RegenFractionPerSecond = RegenFraction,
                    LevelBonusPerLevel = 0.06f
                },
                Fighters = new[]
                {
                    new FighterDef
                    {
                        Id = "test", NameKey = "fighter.test.name", MaxHealth = MaxHealth, MoveSpeed = moveSpeed,
                        Radius = Radius, AttackRange = 8f, AttackRechargeTicks = rechargeTicks, AttackDamage = 380
                    },
                    new FighterDef
                    {
                        Id = "test2", NameKey = "fighter.test2.name", MaxHealth = 5600, MoveSpeed = 2.2f,
                        Radius = 0.55f, AttackRange = 2.5f, AttackRechargeTicks = 33, AttackDamage = 1200
                    }
                },
                Bounds = ArenaBounds.FromSize(ArenaWidth, ArenaHeight)
            };
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
