using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Arena.Editor.Setup
{
    /// <summary>
    /// Applies all project settings from code (idempotent): player/Android settings, URP assets, Input System only.
    /// Run: Tools/unity-method.ps1 -Method Arena.Editor.Setup.ProjectConfigurator.Apply
    /// </summary>
    public static class ProjectConfigurator
    {
        public const string UrpFolder = "Assets/_Project/Settings/URP";
        public const string UrpAssetPath = UrpFolder + "/Arena_URP.asset";
        public const string RendererAssetPath = UrpFolder + "/Arena_Renderer.asset";
        public const string ApplicationId = "com.arena.prototype";
        public const int InputSystemOnly = 1;
        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        private const string ActiveInputHandlerProperty = "activeInputHandler";

        public static void Apply()
        {
            ApplyPlayerSettings();
            ApplyAndroidSettings();
            ApplyRenderPipeline();
            ApplyInputHandler();
            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_CONFIG_APPLIED");
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Arena";
            PlayerSettings.productName = "Arena";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        private static void ApplyAndroidSettings()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            // Online play over UDP (Server-Spec §12.3); the client makes no HTTP requests.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        private static void ApplyRenderPipeline()
        {
            EnsureFolder(UrpFolder);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererAssetPath);
            }

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, UrpAssetPath);
            }

            // Mobile settings: no HDR, no MSAA, no realtime shadows (D-027: blob shadows), no additional lights.
            var so = new SerializedObject(urp);
            SetBool(so, "m_SupportsHDR", false);
            SetInt(so, "m_MSAA", 1);
            SetBool(so, "m_MainLightShadowsSupported", false);
            SetInt(so, "m_AdditionalLightsRenderingMode", 0); // LightRenderingMode.Disabled
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_SoftShadowsSupported", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            int count = QualitySettings.names.Length;
            for (int i = 0; i < count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        private static void ApplyInputHandler()
        {
            var settings = LoadProjectSettings();
            SetInt(settings, ActiveInputHandlerProperty, InputSystemOnly);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Reads PlayerSettings.activeInputHandler (0 old, 1 Input System, 2 both).</summary>
        public static int ReadActiveInputHandler()
        {
            return FindOrThrow(LoadProjectSettings(), ActiveInputHandlerProperty).intValue;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static SerializedObject LoadProjectSettings()
        {
            return new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath)[0]);
        }

        private static SerializedProperty FindOrThrow(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null)
                throw new InvalidOperationException("Arena.Editor.Setup.ProjectConfigurator: property not found: " + name);
            return p;
        }

        private static void SetBool(SerializedObject so, string name, bool value) => FindOrThrow(so, name).boolValue = value;

        private static void SetInt(SerializedObject so, string name, int value) => FindOrThrow(so, name).intValue = value;
    }
}
