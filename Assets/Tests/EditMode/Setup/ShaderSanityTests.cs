using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arena.Tests.EditMode.Setup
{
    /// <summary>
    /// Shader checks (Art-Spec §6.3): every project shader <c>Arena/*</c> imports without errors, and every material under
    /// <c>Assets/_Project</c> uses an allowed shader without errors. Device variants (Vulkan/GLES3) are checked by the APK build.
    /// </summary>
    public sealed class ShaderSanityTests
    {
        private const string ProjectRoot = "Assets/_Project";

        [Test]
        public void ArenaShaders_HaveNoErrors()
        {
            foreach (Shader shader in LoadAll<Shader>("t:Shader"))
            {
                StringAssert.StartsWith(ShaderRules.ArenaPrefix, shader.name, AssetDatabase.GetAssetPath(shader) + ": project shaders are Arena/*");
                AssertNoErrors(shader);
            }
        }

        [Test]
        public void ProjectMaterials_UseAllowedShaders_WithoutErrors()
        {
            List<Material> materials = LoadAll<Material>("t:Material");
            Assert.Greater(materials.Count, 0, "no materials under " + ProjectRoot);
            foreach (Material m in materials)
            {
                string path = AssetDatabase.GetAssetPath(m);
                Assert.IsNotNull(m.shader, path);
                Assert.IsTrue(ShaderRules.IsAllowed(m.shader.name), path + " uses " + m.shader.name);
                AssertNoErrors(m.shader);
            }
        }

        private static void AssertNoErrors(Shader shader)
        {
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), shader.name + " has errors: " + FirstError(shader));
        }

        private static string FirstError(Shader shader)
        {
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    return message.message + " (" + message.file + ":" + message.line + ")";
            return "?";
        }

        private static List<T> LoadAll<T>(string filter) where T : Object
        {
            var result = new List<T>();
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { ProjectRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Fonts (A.6, Nunito) carry an importer-made "Font Material" (built-in GUI/Text Shader) — not a project material.
                if (AssetImporter.GetAtPath(path) is TrueTypeFontImporter) continue;
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) result.Add(asset);
            }
            return result;
        }
    }
}
