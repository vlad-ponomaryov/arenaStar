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
    /// meadow, water and fog recoloured for snow / ice / a snowstorm, a cool-shaded prop material, the young spruce grove bushes
    /// (<see cref="SnowGroveMeshes"/>, D-142), the frame wall, garlands and cabins (<see cref="SnowFrameMeshes"/>, D-142), footprint
    /// trails and the prop meshes of <see cref="SnowPropMeshes"/>; writes <see cref="DataSeeder.SnowThemePath"/> and links it to the Snow Valley
    /// maps (Last Beast and Acorns). Separate from <c>ViewAssetBuilder</c> (it only reads the catalog). Run after <c>ViewAssetBuilder.BuildAll</c>:
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

        private const float SnowmanYawJitter = 25f;

        // D-142: young spruce grove instead of the grass bush — little leaf detail, almost no sway, cool shade.
        private const float GroveLeafContrast = 0.2f;
        private const float GroveSway = 0.01f;
        private static readonly Color GroveShade = new Color(0.6f, 0.68f, 0.82f, 1f);

        // D-142: frame — snow-brick wall with posts and garlands on all sides (lower on the camera side), cabins / woodpiles / mounds behind.
        private const float FrameWallOffset = 0.62f;
        private const float FrameWallSegment = 1f;
        private const float FramePostSpacing = 2f;
        private const float FrameNearHeight = 0.55f;
        private const float FrameLumpDistance = 0.38f;
        private const float FrameExtraOffset = 2.7f;
        private const float FrameExtraSpacing = 5.5f;
        private const float WallLumpChance = 0.35f;

        // D-142: footprint trails (bluish shade of the snow).
        private static readonly Color TrailColor = new Color32(0xB8, 0xC8, 0xDC, 0xFF);
        private const float TrailStep = 0.42f;
        private const float TrailSideOffset = 0.11f;
        private static readonly Vector2 TrailPrintSize = new Vector2(0.065f, 0.1f);
        private const int TrailStopCells = 4;
        private const float TrailSpread = 0.2f;

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
            Material grove = GroveMaterial(catalog.Bush, "SnowGrove");
            Material groveSeeThrough = GroveMaterial(catalog.BushSeeThrough, "SnowGroveSeeThrough");
            foreach (Material m in new[] { floor, meadow, water, fog, props, grove, groveSeeThrough }) EditorUtility.SetDirty(m);

            var theme = LoadOrCreate<MapThemeConfig>(DataSeeder.SnowThemePath);
            theme.Id = ThemeId;
            theme.Floor = floor;
            theme.Meadow = meadow;
            theme.Water = water;
            theme.Fog = fog;
            theme.Props = props;
            theme.Bush = grove;
            theme.BushSeeThrough = groveSeeThrough;
            theme.BushVariants = SaveMeshes(SnowGroveMeshes.BuildAll(Mathf.Max(1, catalog.BushVariantsPerMask)));
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
            theme.FortLantern = SaveMesh(SnowPropMeshes.BuildFortLantern());
            theme.WallLump = SaveMesh(SnowPropMeshes.BuildWallLump());
            theme.Hollow = SaveMesh(SnowPropMeshes.BuildHollowSpruce());
            theme.WallLumpChance = WallLumpChance;
            theme.SnowmanYawJitter = SnowmanYawJitter;
            // D-142: no frame trees (user: they looked awful) — the frame wall instead.
            theme.FrameTrees = new Mesh[0];
            theme.FrameTreeSpacing = 0f;
            theme.FrameTreeRings = 0;
            theme.FrameWall = SaveMesh(SnowFrameMeshes.BuildWall(FrameWallSegment));
            theme.FramePost = SaveMesh(SnowFrameMeshes.BuildPost());
            theme.FrameGarland = SaveMesh(SnowFrameMeshes.BuildGarland(FramePostSpacing));
            theme.FrameWallOffset = FrameWallOffset;
            theme.FrameWallSegment = FrameWallSegment;
            theme.FramePostSpacing = FramePostSpacing;
            theme.FrameNearHeight = FrameNearHeight;
            theme.FrameLumpDistance = FrameLumpDistance;
            // The mound goes last: MapFrameDecor turns it freely, the others face the field.
            theme.FrameExtras = SaveMeshes(new[] { SnowFrameMeshes.BuildCabin(), SnowFrameMeshes.BuildWoodpile(), SnowFrameMeshes.BuildMound() });
            theme.FrameExtraOffset = FrameExtraOffset;
            theme.FrameExtraSpacing = FrameExtraSpacing;
            theme.Trails = true;
            theme.TrailColor = TrailColor;
            theme.TrailStep = TrailStep;
            theme.TrailSideOffset = TrailSideOffset;
            theme.TrailPrintSize = TrailPrintSize;
            theme.TrailStopCells = TrailStopCells;
            theme.TrailSpread = TrailSpread;
            DeleteStale();
            EditorUtility.SetDirty(theme);

            string linked = "";
            foreach (string mapPath in new[] { DataSeeder.RoyaleSnowValleyPath, DataSeeder.AcornsSnowValleyPath })
            {
                var map = AssetDatabase.LoadAssetAtPath<MapConfig>(mapPath);
                if (map == null) continue;
                map.Theme = theme;
                EditorUtility.SetDirty(map);
                linked += " " + map.Id;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("SnowThemeBuilder: wrote " + DataSeeder.SnowThemePath + (linked.Length > 0 ? ", linked to" + linked : " (maps not seeded yet)") +
                      "; hollow spruce " + theme.Hollow.triangles.Length / 3 + " tris");
        }

        private static Material GroveMaterial(Material source, string name)
        {
            Material mat = CopyMaterial(source, name);
            mat.SetFloat("_LeafContrast", GroveLeafContrast);
            mat.SetFloat("_SwayAmplitude", GroveSway);
            mat.SetColor("_ShadeColor", GroveShade);
            return mat;
        }

        /// <summary>D-142: meshes of the D-140 look that the theme no longer uses (grass-bush recolours, frame spruces).</summary>
        private static void DeleteStale()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if ((file.StartsWith("Bush_") && file.EndsWith("_Snow")) || file.StartsWith("FrameSpruce_")) AssetDatabase.DeleteAsset(path);
            }
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
