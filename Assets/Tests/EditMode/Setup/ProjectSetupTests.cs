using System.IO;
using System.Reflection;
using Arena.Core.Sim;
using Arena.Editor.Build;
using Arena.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Arena.Tests.EditMode.Setup
{
    public sealed class ProjectSetupTests
    {
        [Test]
        public void SimLimits_TickRate_Is30()
        {
            Assert.AreEqual(30, SimLimits.TickRate);
        }

        [Test]
        public void BuildSettings_ContainBootMenuMatch_InOrder_AndFilesExist()
        {
            string[] expected =
            {
                "Assets/_Project/Scenes/Boot.unity",
                "Assets/_Project/Scenes/Menu.unity",
                "Assets/_Project/Scenes/Match.unity"
            };
            var scenes = EditorBuildSettings.scenes;
            Assert.AreEqual(expected.Length, scenes.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], scenes[i].path);
                Assert.IsTrue(scenes[i].enabled, expected[i] + " enabled");
                Assert.IsTrue(File.Exists(expected[i]), expected[i] + " exists");
                Assert.AreEqual(expected[i], SceneBuilder.ScenePath(SceneBuilder.SceneOrder[i]));
            }
        }

        [Test]
        public void DefaultRenderPipeline_IsUrp()
        {
            Assert.IsInstanceOf<UniversalRenderPipelineAsset>(GraphicsSettings.defaultRenderPipeline);
            Assert.AreEqual(ProjectConfigurator.UrpAssetPath,
                AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline));
        }

        [Test]
        public void Android_UsesIl2CppAndArm64()
        {
            Assert.AreEqual(ScriptingImplementation.IL2CPP, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android));
            Assert.AreEqual(AndroidArchitecture.ARM64, PlayerSettings.Android.targetArchitectures);
        }

        [Test]
        public void Android_ForcesInternetPermission()
        {
            Assert.IsTrue(PlayerSettings.Android.forceInternetPermission, "online play over UDP (Server-Spec §12.3)");
        }

        [Test]
        public void OnlineAssembly_DoesNotReferenceTheEditorOnlyServer()
        {
            foreach (AssemblyName reference in typeof(Arena.Online.OnlineSession).Assembly.GetReferencedAssemblies())
                Assert.AreNotEqual("Arena.Net.Server", reference.Name, "Arena.Online goes into the APK (Server-Spec §12.1)");
        }

        [Test]
        public void ActiveInputHandler_IsInputSystemOnly()
        {
            Assert.AreEqual(1, ProjectConfigurator.ReadActiveInputHandler());
        }

        [Test]
        public void CoreAssembly_DoesNotReferenceUnityEngine()
        {
            foreach (AssemblyName reference in typeof(SimLimits).Assembly.GetReferencedAssemblies())
                StringAssert.DoesNotStartWith("UnityEngine", reference.Name);
        }
    }
}
