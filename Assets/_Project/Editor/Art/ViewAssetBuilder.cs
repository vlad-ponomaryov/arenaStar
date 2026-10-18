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
        public const string ProjectilePrefabPath = PrefabsFolder + "/Projectile.prefab";
        public const string HealthBarPrefabPath = PrefabsFolder + "/HealthBar.prefab";
        public const string BreakableWallPrefabPath = PrefabsFolder + "/BreakableWall.prefab";
        public const string BushPrefabPath = PrefabsFolder + "/Bush.prefab";

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
        private static readonly Color ProjectileColor = new Color(0.55f, 0.88f, 1f, 1f);
        private static readonly Color BarBackgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        private static readonly Color BarChargeColor = new Color(1f, 0.78f, 0.2f, 1f);

        // Map (S2.5): walls grey-brown, breakable walls lighter wood, bushes dark green, water blue; translucent variants for
        // hiding (D-014, D-024).
        private static readonly Color WallColor = new Color(0.45f, 0.40f, 0.36f, 1f);
        private static readonly Color WallBreakableColor = new Color(0.72f, 0.55f, 0.34f, 1f);
        private static readonly Color BushColor = new Color(0.16f, 0.45f, 0.18f, 1f);
        private static readonly Color BushSeeThroughColor = new Color(0.16f, 0.45f, 0.18f, 0.35f);
        private static readonly Color WaterColor = new Color(0.22f, 0.50f, 0.85f, 1f);
        private static readonly Color FighterHiddenColor = new Color(0.86f, 0.80f, 0.70f, 0.45f);

        // Map geometry (world units, view only, PROTO): wall and breakable wall height, bush height, water sheet height,
        // see-through radius around the local fighter in a bush.
        private const float WallHeight = 1.2f;
        private const float BushHeight = 0.8f;
        private const float WaterHeight = 0.02f;
        private const float BushSeeThroughRadius = 2.5f;

        // URP surface setup for translucent materials (property names of URP Lit/Unlit).
        private const string SurfaceProperty = "_Surface";
        private const string BlendProperty = "_Blend";
        private const string SrcBlendProperty = "_SrcBlend";
        private const string DstBlendProperty = "_DstBlend";
        private const string SrcBlendAlphaProperty = "_SrcBlendAlpha";
        private const string DstBlendAlphaProperty = "_DstBlendAlpha";
        private const string ZWriteProperty = "_ZWrite";
        private const string TransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";

        // Projectiles: pool prewarm (S2.3 plan) and flight height (view only).
        private const int ProjectilePoolPrewarm = 64;
        private const float ProjectileHeight = 0.8f;

        // Health bar (world units): bar above the head, charge segments under it, drawn layers slightly towards the camera.
        private const float BarAboveFeet = 2.1f;
        private const float BarWidth = 1.0f;
        private const float BarHeight = 0.14f;
        private const float BarPadding = 0.03f;
        private const float ChargeHeight = 0.08f;
        private const float ChargeGap = 0.03f;
        private const float LayerOffset = 0.005f;
        private const int ChargeSegments = 3;

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
            Material projectile = SaveMaterial("Projectile", UnlitShader, ProjectileColor);
            Material barAlly = SaveMaterial("BarAlly", UnlitShader, AllyColor);
            Material barEnemy = SaveMaterial("BarEnemy", UnlitShader, EnemyColor);
            Material barBackground = SaveMaterial("BarBackground", UnlitShader, BarBackgroundColor);
            Material barCharge = SaveMaterial("BarCharge", UnlitShader, BarChargeColor);
            Material wall = SaveMaterial("Wall", LitShader, WallColor);
            Material wallBreakable = SaveMaterial("WallBreakable", LitShader, WallBreakableColor);
            Material bush = SaveMaterial("Bush", LitShader, BushColor);
            Material bushSeeThrough = SaveMaterial("BushSeeThrough", LitShader, BushSeeThroughColor, true);
            Material water = SaveMaterial("Water", LitShader, WaterColor);
            Material fighterHidden = SaveMaterial("FighterHidden", LitShader, FighterHiddenColor, true);

            FighterView fighterView = BuildFighterViewPrefab(body, ally, aimLine);
            AimIndicatorView aimIndicator = BuildAimIndicatorPrefab(aimLine);
            GameObject floorPrefab = BuildFloorPrefab(floor);
            GameObject projectilePrefab = BuildProjectilePrefab(projectile);
            HealthBarView healthBar = BuildHealthBarPrefab(barAlly, barBackground, barCharge);
            GameObject breakablePrefab = BuildBlockPrefab("BreakableWall", BreakableWallPrefabPath, wallBreakable, WallHeight);
            GameObject bushPrefab = BuildBlockPrefab("Bush", BushPrefabPath, bush, BushHeight);

            ViewCatalog catalog = LoadOrCreate<ViewCatalog>(ViewCatalogPath);
            catalog.FighterViewPrefab = fighterView;
            catalog.AimIndicatorPrefab = aimIndicator;
            catalog.FloorPrefab = floorPrefab;
            catalog.ProjectilePrefab = projectilePrefab;
            catalog.HealthBarPrefab = healthBar;
            catalog.BreakableWallPrefab = breakablePrefab;
            catalog.BushPrefab = bushPrefab;
            catalog.WallHeight = WallHeight;
            catalog.WaterHeight = WaterHeight;
            catalog.BushSeeThroughRadius = BushSeeThroughRadius;
            catalog.Wall = wall;
            catalog.WallBreakable = wallBreakable;
            catalog.Bush = bush;
            catalog.BushSeeThrough = bushSeeThrough;
            catalog.Water = water;
            catalog.FighterHidden = fighterHidden;
            catalog.ProjectilePoolPrewarm = ProjectilePoolPrewarm;
            catalog.ProjectileHeight = ProjectileHeight;
            catalog.Projectile = projectile;
            catalog.BarAlly = barAlly;
            catalog.BarEnemy = barEnemy;
            catalog.BarBackground = barBackground;
            catalog.BarCharge = barCharge;
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
            s.UltNotReadyBaseColor = new Color(0.6f, 0.6f, 0.6f, 0.18f);
            s.UltNotReadyKnobColor = new Color(0.6f, 0.6f, 0.6f, 0.4f);
            s.UltChargeFillColor = new Color(1f, 0.85f, 0.25f, 0.55f);
            s.UltChargeRingSize = 230f;
            s.TrickButtonDisabledColor = new Color(0.45f, 0.5f, 0.47f, 0.35f);
            s.TrickDotSize = 26f;
            s.TrickDotSpacing = 36f;
            s.TrickDotOffset = 105f;
            s.TrickDotOnColor = new Color(0.35f, 0.95f, 0.55f, 0.95f);
            s.TrickDotOffColor = new Color(1f, 1f, 1f, 0.15f);
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

        /// <summary>Cube of 1 × <paramref name="height"/> × 1 cells with the pivot at the bottom centre (map blocks).</summary>
        private static GameObject BuildBlockPrefab(string name, string path, Material material, float height)
        {
            var root = new GameObject(name);
            try
            {
                GameObject cube = CreatePart(PrimitiveType.Cube, "Cube", root.transform, material);
                cube.transform.localScale = new Vector3(1f, height, 1f);
                cube.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                return SavePrefab(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildProjectilePrefab(Material material)
        {
            var root = new GameObject("Projectile");
            try
            {
                // Unity sphere: diameter 1 = the runtime scale is the hit diameter.
                CreatePart(PrimitiveType.Sphere, "Sphere", root.transform, material);
                return SavePrefab(root, ProjectilePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static HealthBarView BuildHealthBarPrefab(Material fill, Material background, Material charge)
        {
            var root = new GameObject("HealthBar");
            try
            {
                // Quads face −Z; the bar takes the camera rotation, so "towards the camera" is −Z.
                GameObject back = CreatePart(PrimitiveType.Quad, "Background", root.transform, background);
                float segmentsHeight = ChargeGap + ChargeHeight;
                back.transform.localScale = new Vector3(BarWidth + BarPadding * 2f, BarHeight + segmentsHeight + BarPadding * 2f, 1f);
                back.transform.localPosition = new Vector3(0f, -segmentsHeight * 0.5f, 0f);

                var pivot = new GameObject("FillPivot");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(-BarWidth * 0.5f, 0f, -LayerOffset);
                GameObject fillGo = CreatePart(PrimitiveType.Quad, "Fill", pivot.transform, fill);
                fillGo.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);
                fillGo.transform.localPosition = new Vector3(BarWidth * 0.5f, 0f, 0f);

                var segments = new Renderer[ChargeSegments];
                float segmentWidth = (BarWidth - ChargeGap * (ChargeSegments - 1)) / ChargeSegments;
                float y = -(BarHeight * 0.5f + ChargeGap + ChargeHeight * 0.5f);
                for (int i = 0; i < ChargeSegments; i++)
                {
                    GameObject seg = CreatePart(PrimitiveType.Quad, "Charge" + i, root.transform, charge);
                    seg.transform.localScale = new Vector3(segmentWidth, ChargeHeight, 1f);
                    float x = -BarWidth * 0.5f + segmentWidth * 0.5f + i * (segmentWidth + ChargeGap);
                    seg.transform.localPosition = new Vector3(x, y, -LayerOffset);
                    segments[i] = seg.GetComponent<Renderer>();
                }

                var view = root.AddComponent<HealthBarView>();
                var so = new SerializedObject(view);
                SetRef(so, "_fillPivot", pivot.transform);
                SetRef(so, "_fill", fillGo.GetComponent<Renderer>());
                SerializedProperty segs = so.FindProperty("_chargeSegments");
                if (segs == null) throw new InvalidOperationException("ViewAssetBuilder: field not found: _chargeSegments");
                segs.arraySize = segments.Length;
                for (int i = 0; i < segments.Length; i++) segs.GetArrayElementAtIndex(i).objectReferenceValue = segments[i];
                SerializedProperty height = so.FindProperty("_height");
                if (height == null) throw new InvalidOperationException("ViewAssetBuilder: field not found: _height");
                height.floatValue = BarAboveFeet;
                so.ApplyModifiedPropertiesWithoutUndo();

                return SavePrefab(root, HealthBarPrefabPath).GetComponent<HealthBarView>();
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

        private static Material SaveMaterial(string name, string shaderName, Color color, bool transparent = false)
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
            SetSurface(mat, transparent);
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>URP opaque / alpha-blended surface, the same switches the URP material inspector sets.</summary>
        private static void SetSurface(Material mat, bool transparent)
        {
            mat.SetFloat(SurfaceProperty, transparent ? 1f : 0f);
            mat.SetFloat(BlendProperty, 0f);
            mat.SetFloat(SrcBlendProperty, (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            mat.SetFloat(DstBlendProperty, (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            mat.SetFloat(SrcBlendAlphaProperty, (float)BlendMode.One);
            mat.SetFloat(DstBlendAlphaProperty, (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            mat.SetFloat(ZWriteProperty, transparent ? 0f : 1f);
            if (transparent) mat.EnableKeyword(TransparentKeyword);
            else mat.DisableKeyword(TransparentKeyword);
            mat.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            mat.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
            mat.SetShaderPassEnabled("ShadowCaster", !transparent);
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
