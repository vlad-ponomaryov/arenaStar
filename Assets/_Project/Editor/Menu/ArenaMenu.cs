using Arena.Editor.Art;
using Arena.Editor.Build;
using Arena.Editor.Data;
using Arena.Editor.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arena.Editor.Menu
{
    /// <summary>
    /// "Arena" menu for a human in the editor: the same generators the agents run through Tools/unity-method.ps1
    /// (order of a full regeneration: View Assets → Seed Data → Scenes) and the Android build without quitting the editor.
    /// The map editor adds its own item (Arena/Map Editor).
    /// </summary>
    public static class ArenaMenu
    {
        private const string Root = "Arena/";

        [MenuItem(Root + "Seed Data", false, 10)]
        public static void SeedData()
        {
            DataSeeder.SeedAll();
            Debug.Log("Arena: data seeded.");
        }

        [MenuItem(Root + "Rebuild Scenes", false, 11)]
        public static void RebuildScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SceneBuilder.BuildAll();
            Debug.Log("Arena: scenes rebuilt.");
        }

        [MenuItem(Root + "Rebuild View Assets", false, 12)]
        public static void RebuildViewAssets()
        {
            ViewAssetBuilder.BuildAll();
            Debug.Log("Arena: view assets rebuilt.");
        }

        [MenuItem(Root + "Server/Export Content Pack", false, 20)]
        public static void ExportContentPack()
        {
            ContentPackExporter.Export();
            Debug.Log("Arena: content pack exported to " + ContentPackExporter.PackPath + ".");
        }

        [MenuItem(Root + "Build Android APK", false, 30)]
        public static void BuildAndroidApk()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = BuildScript.BuildFromEditor(false);
            EditorUtility.DisplayDialog("Arena", ok ? "APK built: " + BuildScript.DefaultOutputPath : "Build failed, see Console.", "OK");
        }
    }
}
