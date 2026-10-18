using System;
using Arena.Editor.Setup;
using Arena.Gameplay.View;
using Arena.UI.Controls;
using Arena.UI.Style;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art
{
    /// <summary>
    /// Creates or overwrites the view assets in code (D-014): URP materials, primitive-based prefabs (no colliders),
    /// <see cref="ViewCatalog"/> and <see cref="UiStyle"/>. Idempotent; asset GUIDs are kept on re-runs.
    /// Run: Tools/unity-method.ps1 -Method Arena.Editor.Art.ViewAssetBuilder.BuildAll
    /// Values here are editor seeds (allowed outside the runtime logic, CLAUDE.md §1).
    /// </summary>
    public static class ViewAssetBuilder
    {
        public const string ArtRoot = "Assets/_Project/Art";
        public const string MaterialsFolder = ArtRoot + "/Materials";
        public const string PrefabsFolder = ArtRoot + "/Prefabs";
        public const string UiFolder = ArtRoot + "/UI";
        public const string ViewCatalogPath = ArtRoot + "/ViewCatalog.asset";
        public const string UiStylePath = UiFolder + "/UiStyle.asset";
        public const string FighterViewPrefabPath = PrefabsFolder + "/FighterView.prefab";
        public const string AimIndicatorPrefabPath = PrefabsFolder + "/AimIndicator.prefab";
        public const string FloorPrefabPath = PrefabsFolder + "/Floor.prefab";

        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";
        private const string BaseColorProperty = "_BaseColor";
        private const string CircleSpritePath = "UI/Skin/Knob.psd";

        // Team palette (D-029): blue allies, red enemies with a clear brightness difference.
        private static readonly Color AllyColor = new Color(0.20f, 0.52f, 1.00f, 1f);
        private static readonly Color EnemyColor = new Color(0.95f, 0.22f, 0.18f, 1f);
        private static readonly Color BodyColor = new Color(0.86f, 0.80f, 0.70f, 1f);
        private static readonly Color FloorColor = new Color(0.42f, 0.55f, 0.36f, 1f);
        private static readonly Color AimLineColor = new Color(1f, 1f, 1f, 1f);

        // Fighter view geometry (cells): capsule of diameter 0.9 (≈ body radius 0.45), ring slightly larger.
        private const float BodyDiameter = 0.9f;
        private const float BodyHeight = 1.6f;
        private const float RingDiameter = 1.2f;
        private const float RingThickness = 0.02f;
        private const float RingHeight = 0.02f;
        private static readonly Vector3 NoseScale = new Vector3(0.22f, 0.22f, 0.3f);
        private const float NoseForward = 0.45f;
        private const float NoseHeightFraction = 0.75f;

        // Aim line: 1 unit long along +Z from the pivot (root Z scale = range).
        private const float AimLineWidth = 0.15f;
        private const float AimLineThickness = 0.02f;
        private const float AimLineHeight = 0.04f;

        public static void BuildAll()
        {
            ProjectConfigurator.EnsureFolder(MaterialsFolder);
            ProjectConfigurator.EnsureFolder(PrefabsFolder);
            ProjectConfigurator.EnsureFolder(UiFolder);

            Material ally = SaveMaterial("TeamAlly", UnlitShader, AllyColor);
            Material enemy = SaveMaterial("TeamEnemy", UnlitShader, EnemyColor);
            Material body = SaveMaterial("FighterBody", LitShader, BodyColor);
            Material floor = SaveMaterial("Floor", LitShader, FloorColor);
            Material aimLine = SaveMaterial("AimLine", UnlitShader, AimLineColor);

            FighterView fighterView = BuildFighterViewPrefab(body, ally, aimLine);
            AimIndicatorView aimIndicator = BuildAimIndicatorPrefab(aimLine);
            GameObject floorPrefab = BuildFloorPrefab(floor);

            ViewCatalog catalog = LoadOrCreate<ViewCatalog>(ViewCatalogPath);
            catalog.FighterViewPrefab = fighterView;
            catalog.AimIndicatorPrefab = aimIndicator;
            catalog.FloorPrefab = floorPrefab;
            catalog.TeamAlly = ally;
            catalog.TeamEnemy = enemy;
            catalog.FighterBody = body;
            catalog.Floor = floor;
            catalog.AimLine = aimLine;
            EditorUtility.SetDirty(catalog);

            SeedUiStyle(LoadOrCreate<UiStyle>(UiStylePath));

            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_VIEW_ASSETS_BUILT");
        }

        private static void SeedUiStyle(UiStyle s)
        {
            s.ReferenceResolution = new Vector2(1920f, 1080f);
            s.MatchWidthOrHeight = 0.5f;
            s.CircleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(CircleSpritePath);
            if (s.CircleSprite == null) throw new InvalidOperationException("ViewAssetBuilder: built-in sprite not found: " + CircleSpritePath);

            s.MoveStick = new StickStyle
            {
                Position = new Vector2(320f, 300f), HitSize = 0f, BaseSize = 280f, KnobSize = 130f, Radius = 120f,
                BaseColor = new Color(1f, 1f, 1f, 0.22f), KnobColor = new Color(1f, 1f, 1f, 0.6f)
            };
            s.AttackStick = new StickStyle
            {
                Position = new Vector2(300f, 290f), HitSize = 380f, BaseSize = 260f, KnobSize = 120f, Radius = 110f,
                BaseColor = new Color(1f, 0.45f, 0.35f, 0.3f), KnobColor = new Color(1f, 0.45f, 0.35f, 0.85f)
            };
            s.UltStick = new StickStyle
            {
                Position = new Vector2(620f, 190f), HitSize = 260f, BaseSize = 190f, KnobSize = 95f, Radius = 80f,
                BaseColor = new Color(1f, 0.85f, 0.25f, 0.3f), KnobColor = new Color(1f, 0.85f, 0.25f, 0.85f)
            };
            s.TrickButtonPosition = new Vector2(290f, 600f);
            s.TrickButtonSize = 150f;
            s.TrickButtonColor = new Color(0.35f, 0.9f, 0.55f, 0.75f);
            s.Gesture = new StickGestureSettings(0.2f, 0.25f, 0.25f);
            s.AllyColor = AllyColor;
            s.EnemyColor = EnemyColor;
            EditorUtility.SetDirty(s);
        }

        private static FighterView BuildFighterViewPrefab(Material body, Material ring, Material nose)
        {
            var root = new GameObject("FighterView");
            try
            {
                GameObject capsule = CreatePart(PrimitiveType.Capsule, "Body", root.transform, body);
                // Unity capsule: height 2, diameter 1.
                capsule.transform.localScale = new Vector3(BodyDiameter, BodyHeight * 0.5f, BodyDiameter);
                capsule.transform.localPosition = new Vector3(0f, BodyHeight * 0.5f, 0f);

                GameObject noseGo = CreatePart(PrimitiveType.Cube, "Nose", root.transform, nose);
                noseGo.transform.localScale = NoseScale;
                noseGo.transform.localPosition = new Vector3(0f, BodyHeight * NoseHeightFraction, NoseForward);

                GameObject ringGo = CreatePart(PrimitiveType.Cylinder, "Ring", root.transform, ring);
                // Unity cylinder: height 2, diameter 1.
                ringGo.transform.localScale = new Vector3(RingDiameter, RingThickness * 0.5f, RingDiameter);
                ringGo.transform.localPosition = new Vector3(0f, RingHeight, 0f);

                var view = root.AddComponent<FighterView>();
                var so = new SerializedObject(view);
                SetRef(so, "_body", capsule.transform);
                SetRef(so, "_ring", ringGo.GetComponent<Renderer>());
                so.ApplyModifiedPropertiesWithoutUndo();

                return SavePrefab(root, FighterViewPrefabPath).GetComponent<FighterView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static AimIndicatorView BuildAimIndicatorPrefab(Material lineMaterial)
        {
            var root = new GameObject("AimIndicator");
            try
            {
                GameObject line = CreatePart(PrimitiveType.Cube, "Line", root.transform, lineMaterial);
                line.transform.localScale = new Vector3(AimLineWidth, AimLineThickness, 1f);
                line.transform.localPosition = new Vector3(0f, AimLineHeight, 0.5f);

                var view = root.AddComponent<AimIndicatorView>();
                var so = new SerializedObject(view);
                SetRef(so, "_line", line.GetComponent<Renderer>());
                so.ApplyModifiedPropertiesWithoutUndo();

                return SavePrefab(root, AimIndicatorPrefabPath).GetComponent<AimIndicatorView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildFloorPrefab(Material floor)
        {
            var root = new GameObject("Floor");
            try
            {
                GameObject quad = CreatePart(PrimitiveType.Quad, "Quad", root.transform, floor);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                return SavePrefab(root, FloorPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success || prefab == null) throw new InvalidOperationException("ViewAssetBuilder: failed to save prefab " + path);
            return prefab;
        }

        private static Material SaveMaterial(string name, string shaderName, Color color)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("ViewAssetBuilder: shader not found: " + shaderName);
            string path = MaterialsFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            mat.SetColor(BaseColorProperty, color);
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) throw new InvalidOperationException("ViewAssetBuilder: field not found: " + field);
            p.objectReferenceValue = value;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
