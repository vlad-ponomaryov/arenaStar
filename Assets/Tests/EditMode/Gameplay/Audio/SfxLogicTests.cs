using Arena.Gameplay.Audio;
using Arena.Gameplay.Settings;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Gameplay.Audio
{
    /// <summary>Voice limit, cooldowns, clip choice and distance/pan math of the match audio (brief A-TheoSfx).</summary>
    public sealed class SfxLogicTests
    {
        [Test]
        public void VoiceTable_UsesFreeVoicesFirst_ThenReplacesTheOldestOfLowerOrEqualPriority()
        {
            var t = new SfxVoiceTable(2);
            Assert.AreEqual(0, t.Acquire(1, 0f, 1f));
            Assert.AreEqual(1, t.Acquire(5, 0.1f, 1f));
            Assert.AreEqual(2, t.ActiveCount(0.2f));

            Assert.AreEqual(0, t.Acquire(1, 0.2f, 1f), "full: the low-priority voice is replaced");
            Assert.AreEqual(-1, t.Acquire(0, 0.3f, 1f), "a lower priority than every voice is dropped");
            Assert.AreEqual(0, t.Acquire(2, 0.4f, 1f), "the lowest priority goes first, regardless of age");

            Assert.AreEqual(0, t.ActiveCount(5f), "all clips ended");
            Assert.AreEqual(0, t.Acquire(0, 5f, 1f), "free again");
        }

        [Test]
        public void VoiceTable_EqualPriority_ReplacesTheOldest()
        {
            var t = new SfxVoiceTable(3);
            t.Acquire(2, 0f, 10f);
            t.Acquire(2, 1f, 10f);
            t.Acquire(2, 2f, 10f);
            Assert.AreEqual(0, t.Acquire(2, 3f, 10f));
            Assert.AreEqual(1, t.Acquire(2, 4f, 10f));
        }

        [Test]
        public void CueState_Cooldown_IsPerSlotAndCue()
        {
            var s = new SfxCueState(2, 3);
            Assert.IsTrue(s.TryUse(0, 1, 0.5f, 10f));
            Assert.IsFalse(s.TryUse(0, 1, 0.5f, 10.4f), "cooling down");
            Assert.IsTrue(s.TryUse(1, 1, 0.5f, 10.4f), "another fighter");
            Assert.IsTrue(s.TryUse(0, 2, 0.5f, 10.4f), "another cue");
            Assert.IsTrue(s.TryUse(0, 1, 0.5f, 10.5f), "cooldown over");
            Assert.IsFalse(s.TryUse(5, 0, 0f, 10f), "out of range is refused");
        }

        [Test]
        public void CueState_PickClip_CoversAllVariants_WithoutRepeats()
        {
            var s = new SfxCueState(1, 1);
            var rng = new System.Random(3);
            var seen = new bool[4];
            int last = -1;
            for (int i = 0; i < 200; i++)
            {
                int pick = s.PickClip(0, 0, 4, rng);
                Assert.That(pick, Is.InRange(0, 3));
                Assert.AreNotEqual(last, pick);
                seen[pick] = true;
                last = pick;
            }
            CollectionAssert.DoesNotContain(seen, false);
            Assert.AreEqual(0, s.PickClip(0, 0, 1, rng), "a single clip");
        }

        [Test]
        public void Spatial_FadesLinearly_AndPansWithinStrength()
        {
            Assert.AreEqual(1f, SfxSpatial.DistanceGain(2f, 3f, 13f));
            Assert.AreEqual(0.5f, SfxSpatial.DistanceGain(8f, 3f, 13f), 1e-5f);
            Assert.AreEqual(0f, SfxSpatial.DistanceGain(13f, 3f, 13f));
            Assert.AreEqual(0.3f, SfxSpatial.Pan(3f, 6f, 0.6f), 1e-5f);
            Assert.AreEqual(-0.6f, SfxSpatial.Pan(-60f, 6f, 0.6f), 1e-5f, "clamped");
            Assert.AreEqual(0f, SfxSpatial.Pan(3f, 0f, 0.6f));
        }

        [Test]
        public void SoundSetting_IsStoredAndClamped()
        {
            var store = new PlayerSettingsStore(new MemoryKeyValueStore(), new GameSettings { SoundPercent = 100 });
            Assert.AreEqual(100, store.Current.SoundPercent, "default");
            Assert.AreEqual(1f, store.Current.SoundVolume);
            GameSettings s = store.Current.Clone();
            s.SoundPercent = 50;
            store.Save(s);
            Assert.AreEqual(50, store.Load().SoundPercent);
            Assert.AreEqual(0.5f, store.Current.SoundVolume, 1e-5f);
            s.SoundPercent = 400;
            store.Save(s);
            Assert.AreEqual(GameSettings.MaxVibrationPercent, store.Load().SoundPercent);
            s.SoundPercent = 0;
            store.Save(s);
            Assert.AreEqual(0f, store.Current.SoundVolume, "off");
        }
    }
}
