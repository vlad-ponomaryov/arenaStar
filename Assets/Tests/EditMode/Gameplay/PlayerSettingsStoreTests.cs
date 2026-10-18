using Arena.Gameplay.Settings;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class PlayerSettingsStoreTests
    {
        [Test]
        public void EmptyStore_GivesDefaults_MagnetOn_FullVibration()
        {
            var store = new PlayerSettingsStore(new MemoryKeyValueStore());
            GameSettings s = store.Load();

            Assert.IsFalse(s.MirroredLayout);
            Assert.AreEqual(GameSettings.MaxVibrationPercent, s.VibrationPercent);
            Assert.IsTrue(s.AimMagnet, "GDD §5.1: the aim magnet is on by default");
            Assert.IsFalse(s.ColorblindPalette);
            Assert.IsTrue(s.SameAs(GameSettings.CreateDefault()));
        }

        [Test]
        public void Save_ThenLoad_RoundTrips_AndRaisesChanged()
        {
            var kv = new MemoryKeyValueStore();
            var store = new PlayerSettingsStore(kv);
            GameSettings changed = null;
            store.Changed += s => changed = s;

            var settings = new GameSettings { MirroredLayout = true, VibrationPercent = 25, AimMagnet = false, ColorblindPalette = true };
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
