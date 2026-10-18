using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Data
{
    /// <summary>Seeded bot assets (GDD §7 table) and their conversion to Bots defs.</summary>
    public sealed class BotConfigTests
    {
        private static GameDatabase Seeded()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db);
            return db;
        }

        [Test]
        public void Difficulties_MatchTheGddTable()
        {
            GameDatabase db = Seeded();
            BotDifficultyDef novice = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Novice));
            BotDifficultyDef easy = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Easy));

            Assert.AreEqual(BotDifficulty.Novice, novice.Difficulty);
            Assert.AreEqual(25f, novice.AimSpreadDegrees);
            Assert.AreEqual(18, novice.ReactionTicks, "600 ms");
            Assert.AreEqual(BotDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(15f, easy.AimSpreadDegrees);
            Assert.AreEqual(SimTime.SecondsToTicks(0.45f), easy.ReactionTicks, "450 ms");
            Assert.IsNull(db.FindBotDifficulty(BotDifficulty.Hard), "stage 2 has Novice and Easy only");
            Assert.AreEqual(BotDifficulty.Novice, db.MatchRunner.DefaultBots);
        }

        [Test]
        public void Behaviour_DecisionsEvery200ms_RetreatBelow35Percent_Valid()
        {
            BotBehaviourDef b = ConfigConverter.ToBotBehaviourDef(Seeded().BotBehaviour);
            Assert.AreEqual(6, b.DecisionIntervalTicks);
            Assert.AreEqual(0.35f, b.RetreatHealthFraction);
            Assert.GreaterOrEqual(b.PathNodeLimit, 21 * 31, "A* can cover the whole Glade");
            Assert.DoesNotThrow(() => new BotController(b));
        }
    }
}
