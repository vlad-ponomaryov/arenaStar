using Arena.Data;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Data
{
    public sealed class FighterAnimConfigTests
    {
        [Test]
        public void SeededDatabase_HasConsistentFighterAnim()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db);
            FighterAnimConfig c = db.FighterAnim;
            Assert.IsNotNull(c, "GameDatabase.FighterAnim");
            Assert.AreEqual(DataSeeder.FighterAnimConfigPath, AssetDatabase.GetAssetPath(c));
            Assert.Less(c.RunThreshold, c.RunSpeedRef);
            Assert.LessOrEqual(c.BlinkIntervalMin, c.BlinkIntervalMax);
            Assert.Greater(c.BlendSeconds, 0f);
            Assert.Greater(c.MaxDt, 0f);
            Assert.IsNotNull(c.ShotEnvelope);
            Assert.AreEqual(0f, c.ShotEnvelope.Evaluate(0f), 1e-4f);
            Assert.AreEqual(0f, c.ShotEnvelope.Evaluate(1f), 1e-4f);
        }
    }
}
