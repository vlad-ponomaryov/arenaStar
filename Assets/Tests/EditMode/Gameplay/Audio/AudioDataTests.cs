using Arena.Data;
using Arena.Data.Audio;
using Arena.Editor.Audio;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay.Audio
{
    /// <summary>The seeded audio assets (brief A-TheoSfx): Theo's set is complete, the mixer has its groups, the WAVs are
    /// imported by <see cref="AudioImportRules"/>.</summary>
    public sealed class AudioDataTests
    {
        private static AudioConfig Config()
        {
            GameDatabase db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            Assert.IsNotNull(db, "GameDatabase");
            Assert.IsNotNull(db.Audio, "GameDatabase.Audio is seeded");
            return db.Audio;
        }

        [Test]
        public void TheoSoundSet_HasClipsForEveryCue()
        {
            FighterSoundSet theo = Config().FindFighterSounds("pix");
            Assert.IsNotNull(theo, "Theo (pix) has a sound set");
            for (int k = 0; k < FighterSoundSet.KindCount; k++)
            {
                SfxCue cue = theo.Cue((FighterSoundKind)k);
                Assert.IsTrue(cue.HasClips, ((FighterSoundKind)k).ToString());
                for (int i = 0; i < cue.Clips.Length; i++) Assert.IsNotNull(cue.Clips[i], ((FighterSoundKind)k) + " " + i);
            }
            Assert.AreEqual(4, theo.Step.Clips.Length);
            Assert.AreEqual(3, theo.Shot.Clips.Length);
            Assert.AreEqual(SfxChannel.Voice, theo.VoWin.Channel);
            Assert.Greater(Config().MaxVoices, 1);
        }

        [Test]
        public void EveryStartingFighter_HasACompleteSoundSet([Values("bruno", "chomp", "kora", "nova", "vex")] string id)
        {
            FighterSoundSet set = Config().FindFighterSounds(id);
            Assert.IsNotNull(set, id + " has a sound set (A-Roster3D)");
            for (int k = 0; k < FighterSoundSet.KindCount; k++)
            {
                SfxCue cue = set.Cue((FighterSoundKind)k);
                Assert.IsTrue(cue.HasClips, id + " " + (FighterSoundKind)k);
                for (int i = 0; i < cue.Clips.Length; i++) Assert.IsNotNull(cue.Clips[i], id + " " + (FighterSoundKind)k + " " + i);
            }
        }

        [Test]
        public void Mixer_HasSfxAndVoiceGroups()
        {
            AudioConfig c = Config();
            Assert.IsNotNull(c.Mixer, "mixer built by AudioMixerBuilder");
            Assert.IsNotNull(c.SfxGroup);
            Assert.IsNotNull(c.VoiceGroup);
            Assert.AreEqual(AudioMixerBuilder.SfxGroupName, c.SfxGroup.name);
            Assert.AreEqual(AudioMixerBuilder.VoiceGroupName, c.VoiceGroup.name);
        }

        [Test]
        public void TheoWavs_AreImportedMonoVorbisPreloaded()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDataSeeder.TheoClipFolder });
            Assert.AreEqual(15, guids.Length, "4 steps, 3 shots, hit, ult, 2 hurts, 4 voices");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                AudioImporterSampleSettings s = importer.defaultSampleSettings;
                Assert.IsTrue(importer.forceToMono, path);
                Assert.AreEqual(AudioCompressionFormat.Vorbis, s.compressionFormat, path);
                Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, s.loadType, path + " is short");
                Assert.IsTrue(s.preloadAudioData, path);
                Assert.AreEqual(AudioImportRules.VorbisQuality, s.quality, 1e-3f, path);
            }
        }
    }
}
