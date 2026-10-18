using System.IO;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Editor.Net;
using Arena.Net.Content;
using Arena.Net.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Net
{
    /// <summary>
    /// Content pack (Server-Spec §5.4, S3.2): canonical bytes, every def field through the writer and the reader, the hash
    /// follows every number, the committed pack equals an export of the current assets, and defs read from the pack play
    /// a match exactly like the defs converted from the ScriptableObjects.
    /// </summary>
    public sealed class ContentPackTests
    {
        private const int ShortMatchTicks = 900;
        private const int ShortMatchSeed = 77;

        private static GameDatabase Database()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db, "GameDatabase asset");
            return db;
        }

        private static byte[] CommittedPack()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ContentPackExporter.PackPath);
            Assert.IsNotNull(asset, "ArenaContent.bytes is imported as a TextAsset");
            return asset.bytes;
        }

        [Test]
        public void CommittedPack_EqualsExportOfCurrentAssets()
        {
            byte[] exported = ContentPackExporter.BuildPack();
            byte[] committed = CommittedPack();
            Assert.IsTrue(ContentPackExporter.BytesEqual(exported, committed),
                "ArenaContent.bytes is stale: run DataSeeder.SeedAll (or Arena/Server/Export Content Pack) and commit it");
        }

        [Test]
        public void WriteReadWrite_GivesSameBytes()
        {
            byte[] first = ContentPackWriter.Write(ConfigConverter.ToContentBundle(Database()));
            ContentBundle read = ContentPackReader.Read(first, true);
            byte[] second = ContentPackWriter.Write(read);
            Assert.IsTrue(ContentPackExporter.BytesEqual(first, second), "write → read → write");
            Assert.IsNull(DefReflection.Difference(ConfigConverter.ToContentBundle(Database()), read, "bundle"));
        }

        [Test]
        public void EveryFieldOfEveryDef_RoundTrips()
        {
            ContentBundle filled = DefReflection.Filled<ContentBundle>(-1, out int leaves);
            Assert.Greater(leaves, 150, "the def graph has all its fields");
            byte[] pack = ContentPackWriter.Write(filled);
            ContentBundle read = ContentPackReader.Read(pack, true);
            string difference = DefReflection.Difference(filled, read, "bundle");
            Assert.IsNull(difference, "a def field is not written or read: " + difference);
        }

        [Test]
        public void Hash_ChangesWithAnyValue()
        {
            ContentBundle baseline = DefReflection.Filled<ContentBundle>(-1, out int leaves);
            ulong baseHash = ContentPackReader.ReadHeader(ContentPackWriter.Write(baseline)).ContentHash;
            for (int leaf = 0; leaf < leaves; leaf++)
            {
                ContentBundle changed = DefReflection.Filled<ContentBundle>(leaf, out _);
                ulong hash = ContentPackReader.ReadHeader(ContentPackWriter.Write(changed)).ContentHash;
                Assert.AreNotEqual(baseHash, hash, "changing leaf " + leaf + " keeps the hash");
            }
        }

        [Test]
        public void Hash_InHeader_IsSha256OfBody()
        {
            byte[] pack = CommittedPack();
            ContentPackHeader header = ContentPackReader.ReadHeader(pack);
            Assert.AreEqual(ContentPackHeader.ComputeHash(pack, ContentPackHeader.Size, pack.Length - ContentPackHeader.Size), header.ContentHash);
            Assert.AreEqual(ContentPackHeader.CurrentFormatVersion, header.FormatVersion);
        }

        [Test]
        public void Reader_RejectsBrokenPacks()
        {
            byte[] pack = CommittedPack();

            byte[] badMagic = (byte[])pack.Clone();
            badMagic[0] ^= 0xFF;
            Assert.Throws<InvalidDataException>(() => ContentPackReader.Read(badMagic, true));

            byte[] badBody = (byte[])pack.Clone();
            badBody[pack.Length - 1] ^= 0x01;
            Assert.Throws<InvalidDataException>(() => ContentPackReader.Read(badBody, true), "hash mismatch");

            var truncated = new byte[pack.Length - 3];
            System.Array.Copy(pack, truncated, truncated.Length);
            Assert.Throws<InvalidDataException>(() => ContentPackReader.Read(truncated, false), "length mismatch");

            byte[] badVersion = (byte[])pack.Clone();
            badVersion[4] ^= 0xFF;
            Assert.Throws<InvalidDataException>(() => ContentPackReader.Read(badVersion, false), "format version");

            Assert.Throws<InvalidDataException>(() => ContentPackReader.Read(new byte[4], false), "too short");
        }

        [Test]
        public void Bundle_KeepsFighterOrder_AndBuildsAcornsDefs()
        {
            GameDatabase db = Database();
            ContentBundle bundle = ContentPackReader.Read(CommittedPack(), true);
            bundle.Validate();
            Assert.AreEqual(db.Fighters.Count, bundle.Fighters.Length);
            for (int i = 0; i < db.Fighters.Count; i++) Assert.AreEqual(db.Fighters[i].Id, bundle.Fighters[i].Id, "fighter order = GameDatabase order");

            ContentModeMaps acorns = bundle.MapsFor(ModeKind.Acorns);
            Assert.IsNotNull(acorns, "Acorns has maps");
            Assert.AreEqual(db.AcornsMaps.Count, acorns.MapIndices.Length);
            MatchDefs defs = bundle.CreateMatchDefs(acorns.MapIndices[0], ModeKind.Acorns);
            Assert.AreEqual(db.AcornsMaps[0].Id, defs.Map.Id);
            Assert.AreSame(bundle.Acorns, defs.Acorns);
            Assert.AreEqual(db.MatchRunner.FighterLevel, bundle.Policy.FighterLevel);
            Assert.AreEqual(db.MatchRunner.UnlockAllAbilities, bundle.Policy.UnlockAllAbilities);
            Assert.IsNotNull(bundle.FindBotDifficulty(BotDifficulty.Novice));
            Assert.IsNotNull(bundle.FindBotDifficulty(BotDifficulty.Easy));
            Assert.Throws<System.ArgumentException>(() => bundle.CreateMatchDefs(bundle.Maps.Length, ModeKind.Acorns));
        }

        [Test]
        public void DefsFromPack_PlayLikeDefsFromAssets()
        {
            GameDatabase db = Database();
            MatchDefs fromAssets = ConfigConverter.ToMatchDefs(db);
            BotBehaviourDef behaviourFromAssets = ConfigConverter.ToBotBehaviourDef(db.BotBehaviour);
            BotDifficultyDef noviceFromAssets = ConfigConverter.ToBotDifficultyDef(db.FindBotDifficulty(BotDifficulty.Novice));
            var policyFromAssets = new ContentMatchPolicy { FighterLevel = db.MatchRunner.FighterLevel, UnlockAllAbilities = db.MatchRunner.UnlockAllAbilities };

            ContentBundle bundle = ContentPackReader.Read(CommittedPack(), true);
            MatchDefs fromPack = bundle.CreateMatchDefs(bundle.DefaultMapIndex, ModeKind.Acorns);

            HeadlessMatchResult a = HeadlessMatch.Run(fromAssets, HeadlessMatch.CreateBotSetup(fromAssets, policyFromAssets, ShortMatchSeed),
                behaviourFromAssets, noviceFromAssets, ShortMatchTicks);
            HeadlessMatchResult b = HeadlessMatch.Run(fromPack, HeadlessMatch.CreateBotSetup(fromPack, bundle.Policy, ShortMatchSeed),
                bundle.BotBehaviour, bundle.FindBotDifficulty(BotDifficulty.Novice), ShortMatchTicks);

            Assert.AreEqual(a.Ticks, b.Ticks);
            Assert.Greater(a.TotalDamage, 0, "the bots fight in the short match");
            Assert.AreEqual(StateHash.Format(a.StateHash), StateHash.Format(b.StateHash), "state hash after " + ShortMatchTicks + " ticks");
            TestContext.WriteLine("short match state hash " + StateHash.Format(a.StateHash) + ", " + a.AverageTickMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms/tick");
        }
    }
}
