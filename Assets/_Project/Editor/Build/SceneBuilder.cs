using System;
using Arena.Data;
using Arena.Editor.Art;
using Arena.Editor.Data;
using Arena.Editor.Setup;
using Arena.Gameplay.Cameras;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Gameplay.View;
using Arena.UI.Hud;
using Arena.UI.Menu;
using Arena.UI.Style;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Arena.Editor.Build
{
    /// <summary>
    /// Generates the bootstrap scenes Boot, Menu, Match from scratch and registers them in Build Settings (D-032).
    /// Run: Tools/unity-method.ps1 -Method Arena.Editor.Build.SceneBuilder.BuildAll
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenesFolder = "Assets/_Project/Scenes";
        public static readonly string[] SceneOrder = { SceneNames.Boot, SceneNames.Menu, SceneNames.Match };

        // Editor seed pose of the match camera for the scene view; at runtime MatchCameraController applies CameraConfig.
        private const float MatchCameraPitch = 55f;
        private const float MatchCameraDistance = 22f;
        private const float MatchCameraFieldOfView = 50f;
        private static readonly Vector3 LightEuler = new Vector3(50f, -30f, 0f);
        private static readonly Color BackgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);

        public static string ScenePath(string sceneName) => ScenesFolder + "/" + sceneName + ".unity";

        public static void BuildAll()
        {
            ProjectConfigurator.EnsureFolder(ScenesFolder);
            BuildBoot();
            BuildMenu();
            BuildMatch();

            var scenes = new EditorBuildSettingsScene[SceneOrder.Length];
            for (int i = 0; i < SceneOrder.Length; i++)
                scenes[i] = new EditorBuildSettingsScene(ScenePath(SceneOrder[i]), true);
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_SCENES_BUILT");
        }

        private static Scene NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        private static void Save(Scene scene, string sceneName)
        {
            if (!EditorSceneManager.SaveScene(scene, ScenePath(sceneName)))
                throw new InvalidOperationException("Arena.Editor.Build.SceneBuilder: failed to save " + sceneName);
        }

        private static void BuildBoot()
        {
            var scene = NewScene();
            new GameObject("Boot").AddComponent<BootLoader>();
            Save(scene, SceneNames.Boot);
        }

        private static void BuildMenu()
        {
            var scene = NewScene();
            CreateCamera("MenuCamera");
            CreateEventSystem();

            new GameObject("MenuScreen").AddComponent<MenuScreen>();
            Save(scene, SceneNames.Menu);
        }

        private static void BuildMatch()
        {
            // Load after NewScene: opening a scene in Single mode unloads unused assets loaded before it.
            var scene = NewScene();
            var database = LoadRequired<GameDatabase>(DataSeeder.GameDatabasePath);
            var catalog = LoadRequired<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            var style = LoadRequired<UiStyle>(ViewAssetBuilder.UiStylePath);
            var camera = CreateCamera("MatchCamera");
            camera.fieldOfView = MatchCameraFieldOfView;
            var pitch = Quaternion.Euler(MatchCameraPitch, 0f, 0f);
            camera.transform.SetPositionAndRotation(pitch * new Vector3(0f, 0f, -MatchCameraDistance), pitch);
            var cameraController = camera.gameObject.AddComponent<MatchCameraController>();

            var lightGo = new GameObject("DirectionalLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(LightEuler);

            CreateEventSystem();

            var runner = new GameObject("MatchRoot").AddComponent<MatchRunner>();
            var runnerSo = new SerializedObject(runner);
            SetRef(runnerSo, "_database", database);
            SetRef(runnerSo, "_viewCatalog", catalog);
            SetRef(runnerSo, "_cameraController", cameraController);
            runnerSo.ApplyModifiedPropertiesWithoutUndo();

            var hud = new GameObject("MatchHud").AddComponent<MatchHud>();
            var hudSo = new SerializedObject(hud);
            SetRef(hudSo, "_runner", runner);
            SetRef(hudSo, "_style", style);
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            Save(scene, SceneNames.Match);
        }

        private static void CreateEventSystem()
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        private static T LoadRequired<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException("Arena.Editor.Build.SceneBuilder: missing " + path +
                    " (run DataSeeder.SeedAll and ViewAssetBuilder.BuildAll first)");
            return asset;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) throw new InvalidOperationException("Arena.Editor.Build.SceneBuilder: field not found: " + field);
            p.objectReferenceValue = value;
        }

        private static Camera CreateCamera(string name)
        {
            var go = new GameObject(name);
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            go.AddComponent<AudioListener>();
            return camera;
        }
    }
}
