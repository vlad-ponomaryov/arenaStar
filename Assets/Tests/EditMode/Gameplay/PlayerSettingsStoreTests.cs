using Arena.Data;
using Arena.Editor.Data;
using Arena.Gameplay.Settings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class PlayerSettingsStoreTests
    {
        private static FeelConfig SeededFeel()
        {
            DataSeeder.SeedAll();
            return AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath).Feel;
        }

        [Test]
        public void EmptyStore_GivesSeededDefaults_MagnetOn_FullVibration_60Fps()
        {
            FeelConfig feel = SeededFeel();
            var store = new PlayerSettingsStore(new MemoryKeyValueStore(), GameSettings.FromDefaults(feel));
            GameSettings s = store.Load();

            Assert.IsFalse(s.MirroredLayout);
            Assert.AreEqual(GameSettings.MaxVibrationPercent, s.VibrationPercent);
            Assert.IsTrue(s.AimMagnet, "GDD §5.1: the aim magnet is on by default");
            Assert.IsFalse(s.ColorblindPalette);
            Assert.IsFalse(s.BatterySaver);
            Assert.IsTrue(s.SameAs(GameSettings.FromDefaults(feel)));
            Assert.AreEqual(60, FrameRateApplier.FrameRateFor(s, feel));
            s.BatterySaver = true;
            Assert.AreEqual(30, FrameRateApplier.FrameRateFor(s, feel), "battery saver — 30 FPS");
        }

        [Test]
        public void Defaults_ComeFromFeelConfig()
        {
            var feel = ScriptableObject.CreateInstance<FeelConfig>();
            try
            {
                feel.DefaultVibrationPercent = 40;
                feel.DefaultMirroredLayout = true;
                feel.DefaultAimMagnet = false;
                feel.DefaultBatterySaver = true;
                var store = new PlayerSettingsStore(new MemoryKeyValueStore());
                Assert.AreEqual(0, store.Load().VibrationPercent, "no defaults — all off");
                store.SetDefaults(GameSettings.FromDefaults(feel));
                GameSettings s = store.Current;
                Assert.AreEqual(40, s.VibrationPercent);
                Assert.IsTrue(s.MirroredLayout);
                Assert.IsFalse(s.AimMagnet);
                Assert.IsTrue(s.BatterySaver);
            }
            finally
            {
                Object.DestroyImmediate(feel);
            }
        }

        [Test]
        public void Save_ThenLoad_RoundTrips_AndRaisesChanged()
        {
            var kv = new MemoryKeyValueStore();
            var store = new PlayerSettingsStore(kv);
            GameSettings changed = null;
            store.Changed += s => changed = s;

            var settings = new GameSettings
            {
                MirroredLayout = true, VibrationPercent = 25, AimMagnet = false, ColorblindPalette = true, BatterySaver = true
            };
            store.Save(settings);

            Assert.AreEqual(1, kv.SaveCount, "written to disk once");
            Assert.IsNotNull(changed);
            Assert.IsTrue(changed.SameAs(settings));
            Assert.IsTrue(store.Current.SameAs(settings));
            Assert.AreNotSame(settings, store.Current, "the store keeps its own copy");

            GameSettings reloaded = new PlayerSettingsStore(kv).Load();
            Assert.IsTrue(reloaded.SameAs(settings), "a new store over the same storage reads the saved values");
            Assert.AreEqual(0.25f, reloaded.VibrationStrength, 1e-6f);
        }

        [Test]
        public void OutOfRangeVibration_IsClamped()
        {
            var kv = new MemoryKeyValueStore();
            kv.SetInt(PlayerSettingsStore.VibrationKey, 250);
            Assert.AreEqual(100, new PlayerSettingsStore(kv).Load().VibrationPercent);
            kv.SetInt(PlayerSettingsStore.VibrationKey, -5);
            GameSettings s = new PlayerSettingsStore(kv).Load();
            Assert.AreEqual(0, s.VibrationPercent);
            Assert.AreEqual(0f, s.VibrationStrength);
        }
    }
}
