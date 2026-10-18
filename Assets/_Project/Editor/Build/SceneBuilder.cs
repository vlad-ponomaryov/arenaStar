using System;
using Arena.Editor.Setup;
using Arena.Gameplay.Flow;
using Arena.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
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

        // Editor seed values for the temporary match camera (CameraConfig takes over in stage 2).
        private const float MatchCameraPitch = 55f;
        private const float MatchCameraDistance = 22f;
        private const float MatchCameraFieldOfView = 50f;
        private const float FloorScale = 3f;
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

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            new GameObject("MenuScreen").AddComponent<MenuScreen>();
            Save(scene, SceneNames.Menu);
        }

        private static void BuildMatch()
        {
            var scene = NewScene();
            var camera = CreateCamera("MatchCamera");
            camera.fieldOfView = MatchCameraFieldOfView;
            var pitch = Quaternion.Euler(MatchCameraPitch, 0f, 0f);
            camera.transform.SetPositionAndRotation(pitch * new Vector3(0f, 0f, -MatchCameraDistance), pitch);

            var lightGo = new GameObject("DirectionalLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(LightEuler);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.localScale = new Vector3(FloorScale, 1f, FloorScale);
            var floorRenderer = floor.GetComponent<MeshRenderer>();
            floorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            floorRenderer.receiveShadows = false;

            new GameObject("MatchRoot");
            Save(scene, SceneNames.Match);
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
