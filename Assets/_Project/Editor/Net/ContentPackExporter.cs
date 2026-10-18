using System.IO;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Editor.Setup;
using Arena.Net.Content;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.Net
{
    /// <summary>
    /// Writes the content pack <see cref="PackPath"/> (Server-Spec §5.4, D-083): <c>ConfigConverter.ToContentBundle</c> of
    /// the current <c>GameDatabase</c> → <see cref="ContentPackWriter"/>. One file for the online client (TextAsset) and the
    /// server (copied by Tools/server-build.ps1); it lives in git. <c>DataSeeder.SeedAll</c> calls <see cref="Export"/> at
    /// the end, so a reseed always re-exports. The file is rewritten only when its bytes change.
    /// Run: Tools/unity-method.ps1 -Method Arena.Editor.Net.ContentPackExporter.Export (menu Arena/Server/Export Content Pack).
    /// </summary>
    public static class ContentPackExporter
    {
        public const string PackFolder = DataSeeder.DataRoot + "/Net";
        public const string PackPath = PackFolder + "/ArenaContent.bytes";

        /// <summary>The pack of the current assets, in memory (export and the guard test).</summary>
        public static byte[] BuildPack()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            if (db == null) throw new FileNotFoundException("GameDatabase not found at " + DataSeeder.GameDatabasePath + " (run DataSeeder.SeedAll)");
            return ContentPackWriter.Write(ConfigConverter.ToContentBundle(db));
        }

        public static void Export()
        {
            byte[] pack = BuildPack();
            ContentPackHeader header = ContentPackReader.ReadHeader(pack);
            ProjectConfigurator.EnsureFolder(PackFolder);
            string fullPath = Path.GetFullPath(PackPath);
            bool changed = !File.Exists(fullPath) || !BytesEqual(File.ReadAllBytes(fullPath), pack);
            if (changed)
            {
                File.WriteAllBytes(fullPath, pack);
                AssetDatabase.ImportAsset(PackPath, ImportAssetOptions.ForceUpdate);
            }
            Debug.Log("ARENA_CONTENT_EXPORTED hash=" + ContentPackHeader.FormatHash(header.ContentHash) + " bytes=" + pack.Length +
                (changed ? " (written)" : " (unchanged)"));
        }

        public static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
