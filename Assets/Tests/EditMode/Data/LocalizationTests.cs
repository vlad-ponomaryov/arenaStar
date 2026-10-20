using System.Collections.Generic;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Data
{
    /// <summary>Strings.asset (D-019): every entry has EN and RU, every UI key and fighter name exists; Loc switches language.</summary>
    public sealed class LocalizationTests
    {
        private static LocTable LoadTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<LocTable>(DataSeeder.StringsPath);
            Assert.IsNotNull(table, DataSeeder.StringsPath + " — run DataSeeder.SeedAll");
            return table;
        }

        [Test]
        public void EveryEntry_HasNonEmptyEnAndRu_AndUniqueKey()
        {
            LocTable table = LoadTable();
            Assert.Greater(table.Entries.Count, 0);
            var keys = new HashSet<string>();
            foreach (LocEntry e in table.Entries)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Key), "empty key");
                Assert.IsTrue(keys.Add(e.Key), "duplicate key " + e.Key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.En), e.Key + " EN");
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Ru), e.Key + " RU");
            }
        }

        [Test]
        public void AllUiKeys_AndFighterNames_Exist()
        {
            LocTable table = LoadTable();
            foreach (string key in LocKeys.All) Assert.IsNotNull(table.Find(key), "missing key " + key);

            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.AreSame(table, db.Strings, "GameDatabase.Strings");
            foreach (FighterConfig f in db.Fighters) Assert.IsNotNull(table.Find(f.NameKey), "missing fighter name " + f.NameKey);
            Assert.AreEqual("Тео", table.Find("fighter.pix.name").Ru); // D-148
            Assert.AreEqual("Acorns", table.Find(LocKeys.ModeAcorns).En);
            Assert.AreEqual("Жёлуди", table.Find(LocKeys.ModeAcorns).Ru);
        }

        [Test]
        public void Loc_Get_FollowsLanguage_MissingKeyReturnsKey()
        {
            var table = ScriptableObject.CreateInstance<LocTable>();
            try
            {
                table.Entries.Add(new LocEntry { Key = "k", En = "Play", Ru = "Играть" });
                Loc.Init(table, Language.En);
                Assert.AreEqual("Play", Loc.Get("k"));
                Assert.IsTrue(Loc.Has("k"));
                Loc.Init(table, Language.Ru);
                Assert.AreEqual("Играть", Loc.Get("k"));
                Assert.AreEqual("nope", Loc.Get("nope"));
                Assert.IsFalse(Loc.Has("nope"));
            }
            finally
            {
                Object.DestroyImmediate(table);
                Loc.Init(LoadTable(), Language.En);
            }
        }
    }
}
