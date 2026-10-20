using System.IO;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.Audio
{
    /// <summary>
    /// Import settings of the game sounds in <see cref="Folder"/> (brief A-TheoSfx): mono, Vorbis at <see cref="VorbisQuality"/>,
    /// preloaded; short files (≤ <see cref="ShortFileBytes"/>) decompress on load (no decoding while playing), longer ones stay
    /// compressed in memory. The game plays them on 2D sources, so no ambisonics.
    /// </summary>
    public sealed class AudioImportRules : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Audio";

        /// <summary>Vorbis quality 0–1.</summary>
        public const float VorbisQuality = 0.7f;

        /// <summary>WAV size up to which a sound is "short" (16-bit mono 44.1 kHz ≈ 2.3 s).</summary>
        public const long ShortFileBytes = 200 * 1024;

        private const uint Version = 1;

        public override uint GetVersion() => Version;

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder + "/")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.ambisonic = false;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.loadType = IsShort(assetPath) ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = VorbisQuality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = true;
            importer.defaultSampleSettings = s;
        }

        private static bool IsShort(string path)
        {
            var info = new FileInfo(path);
            return !info.Exists || info.Length <= ShortFileBytes;
        }

        /// <summary>Re-imports every sound under <see cref="Folder"/> with these rules.</summary>
        public static void ReimportAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Folder });
            for (int i = 0; i < guids.Length; i++)
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guids[i]), ImportAssetOptions.ForceUpdate);
            Debug.Log("ARENA_AUDIO_REIMPORTED " + guids.Length);
        }
    }
}
