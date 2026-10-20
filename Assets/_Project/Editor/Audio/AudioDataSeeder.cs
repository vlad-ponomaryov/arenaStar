using Arena.Data;
using Arena.Data.Audio;
using Arena.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.Audio
{
    /// <summary>
    /// Seeds the match audio (brief A-TheoSfx step 2): re-imports the sounds with <see cref="AudioImportRules"/>, builds the mixer
    /// (<see cref="AudioMixerBuilder"/>), writes <see cref="AudioConfig"/> and the fighters' <see cref="FighterSoundSet"/>s.
    /// Called by <c>DataSeeder.SeedAll</c>; <see cref="SeedAudio"/> runs it alone. Idempotent, the seeder is the source of truth (D-010).
    /// </summary>
    public static class AudioDataSeeder
    {
        public const string AudioDataFolder = "Assets/_Project/Data/Audio";
        public const string AudioConfigPath = AudioDataFolder + "/Audio.asset";
        public const string MixerPath = AudioImportRules.Folder + "/Arena.mixer";
        public const string TheoClipFolder = AudioImportRules.Folder + "/Fighters/Theo";

        // Mixer and voices (PROTO, brief A-TheoSfx).
        private const int MaxVoices = 12;
        private const float SfxVolume = 0.9f;
        private const float VoiceVolume = 0.8f;
        private const float FullVolumeDistance = 3f;
        private const float SilentDistance = 14f;
        private const float PanStrength = 0.6f;
        private const float PanDistance = 6f;
        private const int ProceduralPriority = 1;

        // Theo (fighter "pix"). Priorities: steps lowest, the end-of-match voice highest.
        private const string TheoId = "pix";
        private const float TheoStepDistance = 0.9f;
        private const float TheoStepTeleport = 1.5f;
        //                                         volume jitter cooldown priority
        private static readonly float[] TheoStep = { 0.35f, 0.08f, 0.15f, 0f };
        // The attack is a burst of 4 drops 0.1 s apart: one shot sound per burst.
        private static readonly float[] TheoShot = { 0.8f, 0.04f, 0.35f, 2f };
        private static readonly float[] TheoHit = { 0.7f, 0.06f, 0.08f, 3f };
        private static readonly float[] TheoUlt = { 1f, 0f, 0.5f, 5f };
        private static readonly float[] TheoHurt = { 0.75f, 0.05f, 0.15f, 3f };
        private static readonly float[] TheoVoEffort = { 0.8f, 0.03f, 1f, 4f };
        private static readonly float[] TheoVoHurt = { 0.7f, 0.05f, 1.5f, 3f };
        private static readonly float[] TheoVoEnd = { 0.9f, 0f, 0f, 6f };

        /// <summary>Seeds the audio and sets <see cref="GameDatabase.Audio"/>. Run: Tools/unity-method.ps1 -Method Arena.Editor.Audio.AudioDataSeeder.SeedAudio</summary>
        public static void SeedAudio()
        {
            GameDatabase db = AssetDatabase.LoadAssetAtPath<GameDatabase>(Data.DataSeeder.GameDatabasePath);
            AudioConfig config = Seed();
            if (db != null)
            {
                db.Audio = config;
                EditorUtility.SetDirty(db);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Writes the audio assets and returns the config (the caller stores it in the database).</summary>
        public static AudioConfig Seed()
        {
            ProjectConfigurator.EnsureFolder(AudioDataFolder);
            AudioImportRules.ReimportAll();
            UnityEngine.Audio.AudioMixer mixer = AudioMixerBuilder.EnsureMixer(MixerPath);

            AudioConfig c = LoadOrCreate<AudioConfig>(AudioConfigPath);
            c.Mixer = mixer;
            c.SfxGroup = AudioMixerBuilder.FindGroup(mixer, AudioMixerBuilder.SfxGroupName);
            c.VoiceGroup = AudioMixerBuilder.FindGroup(mixer, AudioMixerBuilder.VoiceGroupName);
            c.MaxVoices = MaxVoices;
            c.SfxVolume = SfxVolume;
            c.VoiceVolume = VoiceVolume;
            c.FullVolumeDistance = FullVolumeDistance;
            c.SilentDistance = SilentDistance;
            c.PanStrength = PanStrength;
            c.PanDistance = PanDistance;
            c.ProceduralPriority = ProceduralPriority;
            if (c.FighterSounds == null) c.FighterSounds = new System.Collections.Generic.List<FighterSoundSet>();
            c.FighterSounds.Clear();
            c.FighterSounds.Add(SeedTheo());
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_AUDIO_SEEDED sets=" + c.FighterSounds.Count + " mixer=" + (mixer != null) + " sfx=" + (c.SfxGroup != null)
                      + " voice=" + (c.VoiceGroup != null));
            return c;
        }

        private static FighterSoundSet SeedTheo()
        {
            FighterSoundSet s = LoadOrCreate<FighterSoundSet>(AudioDataFolder + "/Sounds_" + TheoId + ".asset");
            s.FighterId = TheoId;
            s.StepDistance = TheoStepDistance;
            s.StepTeleportDistance = TheoStepTeleport;
            s.Step = Cue(TheoStep, SfxChannel.Sfx, false, "theo_step_01", "theo_step_02", "theo_step_03", "theo_step_04");
            s.Shot = Cue(TheoShot, SfxChannel.Sfx, false, "theo_shot_01", "theo_shot_02", "theo_shot_03");
            s.Hit = Cue(TheoHit, SfxChannel.Sfx, false, "theo_hit");
            s.Ult = Cue(TheoUlt, SfxChannel.Sfx, false, "theo_ult");
            s.Hurt = Cue(TheoHurt, SfxChannel.Sfx, false, "theo_hurt_01", "theo_hurt_02");
            s.VoEffort = Cue(TheoVoEffort, SfxChannel.Voice, false, "theo_vo_effort");
            // Pain voices only for the local player's own Theo: a crowd of "ouch" from bots is noise.
            s.VoHurt = Cue(TheoVoHurt, SfxChannel.Voice, true, "theo_vo_hurt");
            s.VoWin = Cue(TheoVoEnd, SfxChannel.Voice, true, "theo_vo_win");
            s.VoLose = Cue(TheoVoEnd, SfxChannel.Voice, true, "theo_vo_lose");
            EditorUtility.SetDirty(s);
            return s;
        }

        private static SfxCue Cue(float[] p, SfxChannel channel, bool localOnly, params string[] clipNames)
        {
            var clips = new AudioClip[clipNames.Length];
            for (int i = 0; i < clipNames.Length; i++)
            {
                string path = TheoClipFolder + "/" + clipNames[i] + ".wav";
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clips[i] == null) Debug.LogError("AudioDataSeeder: missing clip " + path);
            }
            return new SfxCue
            {
                Clips = clips,
                Volume = p[0],
                PitchJitter = p[1],
                Cooldown = p[2],
                Priority = (int)p[3],
                Channel = channel,
                LocalOnly = localOnly
            };
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
