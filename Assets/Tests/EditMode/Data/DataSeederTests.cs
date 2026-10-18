using Arena.Data;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Data
{
    public sealed class DataSeederTests
    {
        [Test]
        public void SeedTwice_DoesNotDuplicateAssets_DatabaseHasSixFighters()
        {
            DataSeeder.SeedAll();
            DataSeeder.SeedAll();

            Assert.AreEqual(6, AssetDatabase.FindAssets("t:" + nameof(FighterConfig), new[] { DataSeeder.FightersFolder }).Length);
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:" + nameof(CombatRulesConfig), new[] { DataSeeder.DataRoot }).Length);
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:" + nameof(GameDatabase), new[] { DataSeeder.DataRoot }).Length);

            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db);
            Assert.IsNotNull(db.CombatRules);
            Assert.AreEqual(DataSeeder.FighterIds.Length, db.Fighters.Count);
            for (int i = 0; i < DataSeeder.FighterIds.Length; i++)
            {
                string id = DataSeeder.FighterIds[i];
                Assert.IsNotNull(db.Fighters[i]);
                Assert.AreEqual(id, db.Fighters[i].Id);
                Assert.AreEqual("fighter." + id + ".name", db.Fighters[i].NameKey);
                Assert.AreEqual(DataSeeder.FighterAssetPath(id), AssetDatabase.GetAssetPath(db.Fighters[i]));
                Assert.AreSame(db.Fighters[i], db.FindFighter(id));
            }
        }
    }
}
