using System;
using Arena.Data;
using Arena.Editor.Data;
using Arena.Editor.Setup;
using Arena.Gameplay.View;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// Builds the snow theme of "Snow Valley" (D-140, Map-SnowValley-LB §5 п.3–4): material copies of the view catalog's floor,
    /// meadow, water and fog recoloured for snow / ice / a snowstorm, a cool-shaded prop material, snow-capped conifer bush meshes and
    /// the prop meshes of <see cref="SnowPropMeshes"/>; writes <see cref="DataSeeder.SnowThemePath"/> and links it to the Snow Valley
    /// map. Separate from <c>ViewAssetBuilder</c> (it only reads the catalog). Run after <c>ViewAssetBuilder.BuildAll</c>:
    /// <c>Tools/unity-method.ps1 -Method Arena.Editor.Art.MapProps.SnowThemeBuilder.Build</c>.
    /// </summary>
    public static class SnowThemeBuilder
    {
        public const string ThemeId = "snow";
        public const string MaterialsFolder = ViewAssetBuilder.MaterialsFolder + "/Snow";
        public const string MeshesFolder = ViewAssetBuilder.MeshesFolder + "/Snow";

        private const string BaseColor = "_BaseColor";
        private const string LegacyColor = "_Color";
        private const string BaseMap = "_BaseMap";
        private const string PatchMap = "_PatchMap";

        // Snow floor (§5 п.3: #E8EEF2; the toon light brightens it, so the albedo is a little darker), drifts of cooler white.
        private static readonly Color FloorTint = new Color32(0xDC, 0xE4, 0xEC, 0xFF);
        private static readonly Color FloorPatchTint = new Color32(0xF4, 0xF8, 0xFC, 0xFF);
        private const float FloorPatchCoverage = 0.4f;
        private static readonly Color GroundShadow = new Color(0.72f, 0.7f, 0.8f, 1f);   // soft lilac-warm shadow on snow
        private static readonly Color GroundAo = new Color(0.7f, 0.72f, 0.84f, 1f);
        private static readonly Color MeadowTint = new Color32(0xC6, 0xD2, 0xDE, 0xFF);
        private static readonly Color FloorOcclusion = new Color32(0x9C, 0xAE, 0xC8, 0xFF);
        private static readonly Color MeadowEdge = new Color32(0xA8, 0xB8, 0xCC, 0xFF);
        private static readonly Color MeadowInner = new Color32(0xB4, 0xC2, 0xD4, 0xFF);
        private static readonly Color Background = new Color32(0x9E, 0xB0, 0xC6, 0xFF);

        // Props: cool shade instead of the warm clay (white snow would turn peach).
        private static readonly Color PropShade = new Color(0.66f, 0.72f, 0.86f, 1f);
        private static readonly Color PropRim = new Color(0.92f, 0.96f, 1f, 1f);

        // Ice (#A9D3DA, matte): still, pale, no ripples.
        private static readonly Color IceDeep = new Color32(0x86, 0xBC, 0xC8, 0xFF);
        private static readonly Color IceShallow = new Color32(0xB4, 0xDC, 0xE2, 0xFF);
        private static readonly Color IceGlint = new Color32(0xEC, 0xF8, 0xFC, 0xFF);
        private static readonly Color IceFoam = new Color32(0xF2, 0xF8, 0xFC, 0xFF);
        private const float IceGlintStrength = 0.2f;
        private const float IceLineStrength = 0.06f;

        // Snowstorm fog (§5 п.3).
        private static readonly Color FogColor = new Color(0.85f, 0.90f, 0.97f, 0.45f);

        // Frame: spruces on three sides.
        private const float FrameTreeSpacing = 1.7f;
        private const float FrameTreeOffset = 1.3f;
        private const int FrameTreeRings = 2;
        private const float FrameTreeRingStep = 1.5f;
        private const float FrameTreeScaleJitter = 0.18f;
        private const float SnowmanYawJitter = 25f;

        public static void Build()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewAssetBuilder.ViewCatalogPath);
            if (catalog == null) throw new InvalidOperationException("SnowThemeBuilder: run ViewAssetBuilder.BuildAll first, no " + ViewAssetBuilder.ViewCatalogPath);
            ProjectConfigurator.EnsureFolder(MaterialsFolder);
            ProjectConfigurator.EnsureFolder(MeshesFolder);
            ProjectConfigurator.EnsureFolder(DataSeeder.MapsFolder);

            Material floor = CopyMaterial(catalog.Floor, "SnowFloor");
            floor.SetColor(BaseColor, FloorTint);
            floor.SetTexture(BaseMap, null);
            floor.SetTexture(PatchMap, null);
            floor.SetFloat("_MapMeanLuminance", 1f);
            floor.SetFloat("_PatchMeanLuminance", 1f);
            floor.SetFloat("_MacroStrength", 0f);
            floor.SetColor("_PatchTint", FloorPatchTint);
            floor.SetColor("_MapShadowColor", GroundShadow);
            floor.SetColor("_MapAoColor", GroundAo);

            Material meadow = CopyMaterial(catalog.Meadow, "SnowMeadow");
            meadow.SetColor(BaseColor, MeadowTint);
            meadow.SetTexture(BaseMap, null);
            meadow.SetFloat("_MapMeanLuminance", 1f);
            meadow.SetFloat("_MacroStrength", 0f);
            meadow.SetColor("_MapShadowColor", GroundShadow);
            meadow.SetColor("_MapAoColor", GroundAo);

            Material water = CopyMaterial(catalog.Water, "Ice");
            water.SetColor("_DeepColor", IceDeep);
            water.SetColor("_ShallowColor", IceShallow);
            water.SetColor("_GlintColor", IceGlint);
            water.SetColor("_FoamColor", IceFoam);
            water.SetFloat("_FlowSpeed", 0f);
            water.SetFloat("_GlintStrength", IceGlintStrength);
            water.SetFloat("_LineStrength", IceLineStrength);
            water.SetFloat("_RingStrength", 0f);
            water.SetFloat("_FoamBreath", 0f);
            water.SetFloat("_FoamWobble", 0f);

            Material fog = CopyMaterial(catalog.Fog, "Snowstorm");
            fog.SetColor(BaseColor, FogColor);
            if (fog.HasProperty(LegacyColor)) fog.SetColor(LegacyColor, FogColor);

            Material props = CopyMaterial(catalog.ObstacleProp, "SnowProps");
            props.SetColor("_ShadeColor", PropShade);
            props.SetColor("_RimColor", PropRim);
            foreach (Material m in new[] { floor, meadow, water, fog, props }) EditorUtility.SetDirty(m);

            var theme = LoadOrCreate<MapThemeConfig>(DataSeeder.SnowThemePath);
            theme.Id = ThemeId;
            theme.Floor = floor;
            theme.Meadow = meadow;
            theme.Water = water;
            theme.Fog = fog;
            theme.Props = props;
            theme.Bush = null;
            theme.BushSeeThrough = null;
            theme.BushVariants = SnowBushes(catalog.BushVariants);
            theme.Background = Background;
            theme.FloorOcclusionColor = FloorOcclusion;
            theme.MeadowEdge = MeadowEdge;
            theme.MeadowInner = MeadowInner;
            theme.FloorPatchCoverage = FloorPatchCoverage;
            theme.FloorDecor = false;
            theme.Tree = SaveMesh(SnowPropMeshes.BuildTree());
            theme.Gifts = SaveMeshes(SnowPropMeshes.BuildGifts());
            theme.GiftBig = SaveMesh(SnowPropMeshes.BuildBigGift());
            theme.Snowmen = SaveMeshes(SnowPropMeshes.BuildSnowmen());
            theme.FortBrick = SaveMesh(SnowPropMeshes.BuildFortBrick());
            theme.FortCorner = SaveMesh(SnowPropMeshes.BuildFortCorner());
            theme.Drift = SaveMesh(SnowPropMeshes.BuildDrift());
            theme.Sled = SaveMesh(SnowPropMeshes.BuildSled());
            theme.SnowPile = SaveMesh(SnowPropMeshes.BuildSnowPile());
            theme.FrameTrees = SaveMeshes(SnowPropMeshes.BuildFrameTrees());
            theme.FrameTreeSpacing = FrameTreeSpacing;
            theme.FrameTreeOffset = FrameTreeOffset;
            theme.FrameTreeRings = FrameTreeRings;
            theme.FrameTreeRingStep = FrameTreeRingStep;
            theme.FrameTreeScaleJitter = FrameTreeScaleJitter;
            theme.SnowmanYawJitter = SnowmanYawJitter;
            EditorUtility.SetDirty(theme);

            var map = AssetDatabase.LoadAssetAtPath<MapConfig>(DataSeeder.RoyaleSnowValleyPath);
            if (map != null)
            {
                map.Theme = theme;
                EditorUtility.SetDirty(map);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("SnowThemeBuilder: wrote " + DataSeeder.SnowThemePath + (map != null ? " and linked it to " + map.Id : " (map not seeded yet)"));
        }

        private static Mesh[] SnowBushes(Mesh[] source)
        {
            if (source == null) return new Mesh[0];
            var result = new Mesh[source.Length];
            for (int i = 0; i < source.Length; i++)
                result[i] = source[i] != null ? SaveMesh(SnowPropMeshes.SnowBush(source[i], EnvMeshBuilder.BushMaxHeight)) : null;
            return result;
        }

        private static Material CopyMaterial(Material source, string name)
        {
            if (source == null) throw new InvalidOperationException("SnowThemeBuilder: the view catalog has no material for " + name);
            string path = MaterialsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                var mat = new Material(source) { name = name };
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }
            existing.shader = source.shader;
            existing.CopyPropertiesFromMaterial(source);
            existing.renderQueue = source.renderQueue;
            return existing;
        }

        private static Mesh[] SaveMeshes(Mesh[] meshes)
        {
            for (int i = 0; i < meshes.Length; i++) meshes[i] = SaveMesh(meshes[i]);
            return meshes;
        }

        private static Mesh SaveMesh(Mesh mesh)
        {
            string path = MeshesFolder + "/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            string name = existing.name;
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
