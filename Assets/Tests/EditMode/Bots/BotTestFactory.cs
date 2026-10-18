using System.Text;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Sim;

namespace Arena.Tests.EditMode.Bots
{
    /// <summary>Bot defs for tests (the seeded values, independent of the assets) and a bots + simulation stepping helper.</summary>
    public static class BotTestFactory
    {
        public const float NoviceSpread = 25f;
        public const int NoviceReaction = 18;
        public const float EasySpread = 15f;
        public const int EasyReaction = 14;
        public const int DecisionInterval = 6;
        public const float ViewRadius = 10f;
        public const float RetreatHealth = 0.35f;
        public const float LastChargeDistance = 3f;

        public static BotBehaviourDef Behaviour() => new BotBehaviourDef
        {
            DecisionIntervalTicks = DecisionInterval,
            ViewRadius = ViewRadius,
            RetreatHealthFraction = RetreatHealth,
            RecoverHealthFraction = 0.8f,
            LastChargeEnemyDistance = LastChargeDistance,
            EngageRangeFraction = 0.8f,
            FireRangeFraction = 0.95f,
            MinShotIntervalTicks = 9,
            StrafeSwitchTicks = 24,
            WaypointReachDistance = 0.3f,
            WanderRadiusCells = 4,
            WanderAttempts = 8,
            RetreatScore = 1f,
            AttackScore = 0.6f,
            AttackProximityBonus = 0.3f,
            ObjectiveScore = 0.5f,
            WanderScore = 0.1f,
            PathNodeLimit = 2048,
            MaxPathLength = 64
        };

        public static BotDifficultyDef Novice() => new BotDifficultyDef
        {
            Difficulty = BotDifficulty.Novice, AimSpreadDegrees = NoviceSpread, ReactionTicks = NoviceReaction
        };

        public static BotDifficultyDef Easy() => new BotDifficultyDef
        {
            Difficulty = BotDifficulty.Easy, AimSpreadDegrees = EasySpread, ReactionTicks = EasyReaction
        };

        /// <summary>Controller with bots in the given slots (seed of the simulation's setup).</summary>
        public static BotController Bots(MatchSimulation sim, BotDifficultyDef difficulty, params int[] slots)
        {
            var bots = new BotController(Behaviour());
            for (int i = 0; i < slots.Length; i++) bots.AddBot(slots[i], difficulty, sim.Setup.Seed);
            return bots;
        }

        /// <summary>One frame of the local loop: zero inputs, bots write theirs, the simulation ticks.</summary>
        public static void Step(MatchSimulation sim, BotController bots, PlayerInput[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
            bots.Update(sim.State, sim.Defs, inputs);
            sim.Tick(inputs);
        }

        /// <summary>Square all-floor map rows of <paramref name="size"/> with a <paramref name="tile"/> block (inclusive cells).</summary>
        public static string[] RowsWithBlock(int width, int height, char tile, int fromColumn, int fromRow, int toColumn, int toRow)
        {
            var rows = new string[height];
            var sb = new StringBuilder(width);
            for (int r = 0; r < height; r++)
            {
                int row = height - 1 - r; // rows are written top first
                sb.Length = 0;
                for (int c = 0; c < width; c++)
                    sb.Append(c >= fromColumn && c <= toColumn && row >= fromRow && row <= toRow ? tile : '.');
                rows[r] = sb.ToString();
            }
            return rows;
        }

        public static MatchDefs DefsOnRows(string[] rows)
        {
            MatchDefs defs = Arena.Tests.EditMode.Core.TestMatchFactory.CreateDefs();
            defs.Map = Arena.Data.Conversion.MapConverter.FromRows("bot_test", rows);
            return defs;
        }
    }
}
