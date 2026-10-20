using System;
using Arena.Editor.Setup;
using Arena.Gameplay.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art
{
    /// <summary>
    /// Muzzle flash asset of the fighter animation (brief A-TheoAnim §3, AN3): a two-plane star mesh stretched along +Z (the barrel),
    /// an additive unlit URP material, the prefab, and <see cref="ViewCatalog.MuzzleFlashPrefab"/>. Run after
    /// <see cref="ViewAssetBuilder.BuildAll"/> (it keeps the field): Tools/unity-method.ps1 -Method Arena.Editor.Art.MuzzleFlashBuilder.Build
    /// </summary>
    public static class MuzzleFlashBuilder
    {
        public const string MeshPath = ViewAssetBuilder.ArtRoot + "/Meshes/MuzzleFlash.asset";
        public const string MaterialPath = ViewAssetBuilder.MaterialsFolder + "/MuzzleFlash.mat";
        public const string PrefabPath = ViewAssetBuilder.PrefabsFolder + "/MuzzleFlash.prefab";

        private const string UnlitShader = "Universal Render Pipeline/Unlit";
        // Warm yellow-white; additive, so it brightens whatever is behind it.
        private static readonly Color FlashColor = new Color(1f, 0.82f, 0.38f, 1f);
        // Star of 8 points, 1 unit long along the barrel (from the muzzle forward) and 0.6 across; inner radius as a fraction of the outer.
        private const int StarPoints = 8;
        private const float InnerRadius = 0.42f;
        private const float Width = 0.6f;

        public static void Build()
        {
            ProjectConfigurator.EnsureFolder(ViewAssetBuilder.ArtRoot + "/Meshes");
            Mesh mesh = BuildMesh();
            Material material = BuildMaterial();

            var go = new GameObject("MuzzleFlash");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);

                var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
                if (catalog == null) throw new InvalidOperationException("MuzzleFlashBuilder: ViewCatalog missing — run ViewAssetBuilder.BuildAll");
                catalog.MuzzleFlashPrefab = prefab;
                EditorUtility.SetDirty(catalog);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_MUZZLE_FLASH_BUILT " + PrefabPath);
        }

        /// <summary>Two star polygons (horizontal XZ and vertical YZ planes) around the origin, pointing along +Z, double-sided.</summary>
        internal static Mesh BuildMesh()
        {
            int rim = StarPoints * 2;
            int perPlane = rim + 1;
            var vertices = new Vector3[perPlane * 2];
            var triangles = new int[rim * 3 * 2 * 2];
            int t = 0;
            for (int plane = 0; plane < 2; plane++)
            {
                int c = plane * perPlane;
                vertices[c] = new Vector3(0f, 0f, 0.5f);
                for (int i = 0; i < rim; i++)
                {
                    float a = i * Mathf.PI * 2f / rim;
                    float r = (i % 2 == 0) ? 1f : InnerRadius;
                    float across = Mathf.Sin(a) * r * 0.5f * Width;
                    // The star starts at the muzzle: its back point sits at the origin, the front one at z = 1.
                    float along = (Mathf.Cos(a) * r + 1f) * 0.5f;
                    vertices[c + 1 + i] = plane == 0 ? new Vector3(across, 0f, along) : new Vector3(0f, across, along);
                }
                for (int i = 0; i < rim; i++)
                {
                    int a = c + 1 + i;
                    int b = c + 1 + (i + 1) % rim;
                    triangles[t++] = c; triangles[t++] = a; triangles[t++] = b;
                    triangles[t++] = c; triangles[t++] = b; triangles[t++] = a;
                }
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }
            mesh.Clear();
            mesh.name = "MuzzleFlash";
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find(UnlitShader);
            if (shader == null) throw new InvalidOperationException("MuzzleFlashBuilder: shader not found: " + UnlitShader);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", FlashColor);
            // URP surface: transparent, additive (_Blend 2), no depth write, both sides.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
