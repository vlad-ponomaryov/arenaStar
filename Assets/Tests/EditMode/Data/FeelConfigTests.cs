using Arena.Data;
using Arena.Data.Localization;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Data
{
    public sealed class FeelConfigTests
    {
        private static FeelConfig SeededFeel()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db);
            Assert.IsNotNull(db.Feel, "GameDatabase.Feel");
            return db.Feel;
        }

        [Test]
        public void SeededFeel_IsInTheDatabase_WithGddValues()
        {
            FeelConfig feel = SeededFeel();
            Assert.AreEqual(DataSeeder.FeelConfigPath, AssetDatabase.GetAssetPath(feel));
            Assert.AreEqual(0.04f, feel.HitStopSeconds, 1e-6f, "GDD §5.3: hit stop 40 ms");
            Assert.AreEqual(0.5f, feel.TelegraphSeconds, 1e-6f, "GDD §5.3: zone 0.5 s before triggering");
            Assert.AreEqual(5f, feel.AimMagnetDegrees, 1e-6f, "GDD §5.1: magnet up to 5°");
        }

        [Test]
        public void SeededFeel_ValuesAreUsable()
        {
            FeelConfig feel = SeededFeel();
            Assert.Greater(feel.FlashSeconds, 0f);
            Assert.Greater(feel.NumberSeconds, 0f);
            Assert.Greater(feel.NumberFontSize, 0);
            Assert.GreaterOrEqual(feel.UltNumberFontSize, feel.NumberFontSize);
            Assert.Greater(feel.NumberPoolPrewarm, 0);
            Assert.Greater(feel.ShakeAmplitude, 0f);
            Assert.Greater(feel.ShakeSeconds, 0f);
            Assert.Greater(feel.HitVibrationMs, 0);
            Assert.GreaterOrEqual(feel.UltVibrationMs, feel.HitVibrationMs);
            Assert.GreaterOrEqual(feel.SampleRate, 8000);
            Assert.Greater(feel.TextReferenceResolution.x, 0f);
            Assert.Greater(feel.FpsUpdateSeconds, 0f);
            foreach (ProceduralSoundSettings s in new[] { feel.HitSound, feel.UltSound, feel.UltReadySound })
            {
                Assert.Greater(s.Seconds, 0f);
                Assert.Greater(s.Volume, 0f);
                Assert.Greater(s.StartFrequency, 0f);
            }
            Assert.IsNotNull(feel.VibrationSteps);
            Assert.Greater(feel.VibrationSteps.Length, 1);
            Assert.AreEqual(0, feel.VibrationSteps[0], "first step = off");
            Assert.AreEqual(100, feel.VibrationSteps[feel.VibrationSteps.Length - 1]);
        }

        [Test]
        public void SettingsAndFpsKeys_AreListed()
        {
            CollectionAssert.Contains(LocKeys.All, LocKeys.MenuSettings);
            CollectionAssert.Contains(LocKeys.All, LocKeys.SettingsAimMagnet);
            CollectionAssert.Contains(LocKeys.All, LocKeys.HudFps);
        }
    }
}
