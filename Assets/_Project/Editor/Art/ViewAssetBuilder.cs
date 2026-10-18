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
        public const string TexturesFolder = ArtRoot + "/Textures";
        public const string BlobShadowTexturePath = TexturesFolder + "/BlobShadow.png";
        public const string ViewCatalogPath = ArtRoot + "/ViewCatalog.asset";
        public const string UiStylePath = UiFolder + "/UiStyle.asset";
        public const string FighterViewPrefabPath = PrefabsFolder + "/FighterView.prefab";
        public const string AimIndicatorPrefabPath = PrefabsFolder + "/AimIndicator.prefab";
        public const string FloorPrefabPath = PrefabsFolder + "/Floor.prefab";
        public const string ProjectilePrefabPath = PrefabsFolder + "/Projectile.prefab";
        public const string HealthBarPrefabPath = PrefabsFolder + "/HealthBar.prefab";
        public const string BreakableWallPrefabPath = PrefabsFolder + "/BreakableWall.prefab";
        public const string BushPrefabPath = PrefabsFolder + "/Bush.prefab";
        public const string AcornPrefabPath = PrefabsFolder + "/Acorn.prefab";
        public const string AcornGoldenPrefabPath = PrefabsFolder + "/AcornGolden.prefab";
        public const string HollowPrefabPath = PrefabsFolder + "/Hollow.prefab";
        public const string TelegraphPrefabPath = PrefabsFolder + "/Telegraph.prefab";
        public const string LogPrefabPath = PrefabsFolder + "/Log.prefab";
        public const string LobBombPrefabPath = PrefabsFolder + "/LobBomb.prefab";
        public const string MinePrefabPath = PrefabsFolder + "/Mine.prefab";
        public const string DomePrefabPath = PrefabsFolder + "/Dome.prefab";

        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";
        private const string BaseColorProperty = "_BaseColor";
        private const string BaseMapProperty = "_BaseMap";
        private const string MainTexProperty = "_MainTex";
        private const string CircleSpritePath = "UI/Skin/Knob.psd";

        // Team palette (D-029): blue allies, red enemies with a clear brightness difference.
        private static readonly Color AllyColor = new Color(0.20f, 0.52f, 1.00f, 1f);
        private static readonly Color EnemyColor = new Color(0.95f, 0.22f, 0.18f, 1f);
        private static readonly Color BodyColor = new Color(0.86f, 0.80f, 0.70f, 1f);

        // Colour-blind palette (D-029): allies stay blue, enemies orange (blue/orange is told apart by all common types).
        private static readonly Color EnemyAltColor = new Color(1.00f, 0.60f, 0.10f, 1f);

        // Feel (S2.10, D-059): hit flash = bright unlit body; telegraph = translucent zone + stronger growing fill by team.
        private static readonly Color FlashColor = new Color(1f, 1f, 1f, 1f);
        private const float TelegraphZoneAlpha = 0.18f;
        private const float TelegraphFillAlpha = 0.42f;
        private const float TelegraphThickness = 0.01f;
        private const float TelegraphZoneHeight = 0.03f;
        private const float TelegraphFillHeight = 0.04f;
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

        // Acorns mode (S2.7, view only, PROTO): brown acorn sphere, bigger golden one, dark hollow stump in the centre.
        private static readonly Color AcornColor = new Color(0.62f, 0.38f, 0.16f, 1f);
        private static readonly Color AcornGoldenColor = new Color(1f, 0.80f, 0.15f, 1f);
        private static readonly Color HollowColor = new Color(0.30f, 0.20f, 0.12f, 1f);
        private const float AcornDiameter = 0.5f;
        private const float AcornGoldenDiameter = 0.7f;
        private const float AcornHeight = 0.35f;
        private const float AcornSpinSpeed = 90f;
        private const int AcornPoolPrewarm = 16;
        private const float HollowDiameter = 1f;
        private const float HollowHeight = 0.5f;

        // Projectiles: pool prewarm (S2.3 plan) and flight height (view only).
        private const int ProjectilePoolPrewarm = 64;
        private const float ProjectileHeight = 0.8f;

        // S2.11 views (PROTO, D-060): Chomp's log, lobbed pine cone and mine; Kora's dome uses the team zone materials.
        private const float LogThickness = 0.5f;
        private const float LobBombDiameter = 0.4f;
        private const float LobArcHeight = 2f;
        private const float MineDiameter = 0.6f;
        private const float MineHeight = 0.15f;
        private static readonly Color LogColor = new Color(0.50f, 0.32f, 0.16f, 1f);
        private static readonly Color PineConeColor = new Color(0.42f, 0.26f, 0.12f, 1f);
        private static readonly Color MineColor = new Color(0.30f, 0.20f, 0.10f, 1f);

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

        // Blob shadow under a fighter (D-027, S2.13): code-generated radial texture on a flat quad just above the floor.
        private const int BlobShadowTextureSize = 64;
        private const float BlobShadowSoftStart = 0.55f; // fraction of the radius where the edge starts to fade
        private const float BlobShadowDiameter = 1.15f;
        private const float BlobShadowHeight = 0.01f;
        private static readonly Color BlobShadowColor = new Color(0f, 0f, 0f, 0.45f);

        // Aim line: 1 unit long along +Z from the pivot (root Z scale = range).
        private const float AimLineWidth = 0.15f;
        private const float AimLineThickness = 0.02f;
        private const float AimLineHeight = 0.04f;

        public static void BuildAll()
        {
            ProjectConfigurator.EnsureFolder(MaterialsFolder);
            ProjectConfigurator.EnsureFolder(PrefabsFolder);
            ProjectConfigurator.EnsureFolder(UiFolder);
            ProjectConfigurator.EnsureFolder(TexturesFolder);

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
            Material enemyAlt = SaveMaterial("TeamEnemyAlt", UnlitShader, EnemyAltColor);
            Material barEnemyAlt = SaveMaterial("BarEnemyAlt", UnlitShader, EnemyAltColor);
            Material flash = SaveMaterial("FighterFlash", UnlitShader, FlashColor);
            Material telegraphAllyZone = SaveMaterial("TelegraphAllyZone", UnlitShader, WithAlpha(AllyColor, TelegraphZoneAlpha), true);
            Material telegraphAllyFill = SaveMaterial("TelegraphAllyFill", UnlitShader, WithAlpha(AllyColor, TelegraphFillAlpha), true);
            Material telegraphEnemyZone = SaveMaterial("TelegraphEnemyZone", UnlitShader, WithAlpha(EnemyColor, TelegraphZoneAlpha), true);
            Material telegraphEnemyFill = SaveMaterial("TelegraphEnemyFill", UnlitShader, WithAlpha(EnemyColor, TelegraphFillAlpha), true);
            Material telegraphEnemyAltZone = SaveMaterial("TelegraphEnemyAltZone", UnlitShader, WithAlpha(EnemyAltColor, TelegraphZoneAlpha), true);
            Material telegraphEnemyAltFill = SaveMaterial("TelegraphEnemyAltFill", UnlitShader, WithAlpha(EnemyAltColor, TelegraphFillAlpha), true);
            TelegraphView telegraph = BuildTelegraphPrefab(telegraphEnemyZone, telegraphEnemyFill);

            Material blobShadow = SaveMaterial("BlobShadow", UnlitShader, BlobShadowColor, true);
            Texture2D blobTexture = BuildBlobShadowTexture();
            blobShadow.SetTexture(BaseMapProperty, blobTexture);
            blobShadow.SetTexture(MainTexProperty, blobTexture);
            EditorUtility.SetDirty(blobShadow);
            FighterView fighterView = BuildFighterViewPrefab(body, ally, aimLine, blobShadow);
            AimIndicatorView aimIndicator = BuildAimIndicatorPrefab(aimLine);
            GameObject floorPrefab = BuildFloorPrefab(floor);
            GameObject projectilePrefab = BuildProjectilePrefab(projectile);
            HealthBarView healthBar = BuildHealthBarPrefab(barAlly, barBackground, barCharge);
            GameObject breakablePrefab = BuildBlockPrefab("BreakableWall", BreakableWallPrefabPath, wallBreakable, WallHeight);
            GameObject bushPrefab = BuildBlockPrefab("Bush", BushPrefabPath, bush, BushHeight);
            Material acorn = SaveMaterial("Acorn", LitShader, AcornColor);
            Material acornGolden = SaveMaterial("AcornGolden", UnlitShader, AcornGoldenColor);
            Material hollow = SaveMaterial("Hollow", LitShader, HollowColor);
            GameObject acornPrefab = BuildSpherePrefab("Acorn", AcornPrefabPath, acorn, AcornDiameter);
            GameObject acornGoldenPrefab = BuildSpherePrefab("AcornGolden", AcornGoldenPrefabPath, acornGolden, AcornGoldenDiameter);
            GameObject hollowPrefab = BuildHollowPrefab(hollow);
            Material log = SaveMaterial("Log", LitShader, LogColor);
            Material pineCone = SaveMaterial("PineCone", LitShader, PineConeColor);
            Material mine = SaveMaterial("Mine", LitShader, MineColor);
            GameObject logPrefab = BuildLogPrefab(log);
            GameObject lobBombPrefab = BuildSpherePrefab("LobBomb", LobBombPrefabPath, pineCone, LobBombDiameter);
            GameObject minePrefab = BuildMinePrefab(mine);
            GameObject domePrefab = BuildDomePrefab(telegraphAllyZone);

            ViewCatalog catalog = LoadOrCreate<ViewCatalog>(ViewCatalogPath);
            catalog.FighterViewPrefab = fighterView;
            catalog.AimIndicatorPrefab = aimIndicator;
            catalog.FloorPrefab = floorPrefab;
            catalog.ProjectilePrefab = projectilePrefab;
            catalog.HealthBarPrefab = healthBar;
            catalog.BreakableWallPrefab = breakablePrefab;
            catalog.BushPrefab = bushPrefab;
            catalog.AcornPrefab = acornPrefab;
            catalog.AcornGoldenPrefab = acornGoldenPrefab;
            catalog.HollowPrefab = hollowPrefab;
            catalog.TelegraphPrefab = telegraph;
            catalog.LogPrefab = logPrefab;
            catalog.LogThickness = LogThickness;
            catalog.LobBombPrefab = lobBombPrefab;
            catalog.LobArcHeight = LobArcHeight;
            catalog.MinePrefab = minePrefab;
            catalog.DomePrefab = domePrefab;
            catalog.TeamEnemyAlt = enemyAlt;
            catalog.BarEnemyAlt = barEnemyAlt;
            catalog.FighterFlash = flash;
            catalog.TelegraphAllyZone = telegraphAllyZone;
            catalog.TelegraphAllyFill = telegraphAllyFill;
            catalog.TelegraphEnemyZone = telegraphEnemyZone;
            catalog.TelegraphEnemyFill = telegraphEnemyFill;
            catalog.TelegraphEnemyAltZone = telegraphEnemyAltZone;
            catalog.TelegraphEnemyAltFill = telegraphEnemyAltFill;
            catalog.AcornPoolPrewarm = AcornPoolPrewarm;
            catalog.AcornHeight = AcornHeight;
            catalog.AcornSpinSpeed = AcornSpinSpeed;
            catalog.Acorn = acorn;
            catalog.AcornGolden = acornGolden;
            catalog.Hollow = hollow;
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
            s.EnemyColorAlt = EnemyAltColor;

            // S2.10 (PROTO): FPS counter in the top-left corner, settings panel rows.
            s.FpsMargin = new Vector2(24f, 20f);
            s.FpsBoxSize = new Vector2(220f, 44f);
            s.SettingsPanelColor = new Color(0.08f, 0.09f, 0.13f, 0.97f);
            s.SettingsLabelWidth = 560f;
            s.SettingsChoiceSize = new Vector2(150f, 90f);

            // Menu, score bar, results (S2.7, PROTO sizes at 1920×1080 — check on the phone).
            s.TextColor = Color.white;
            s.TitleFontSize = 72;
            s.LargeFontSize = 52;
            s.MediumFontSize = 38;
            s.SmallFontSize = 28;
            s.ButtonColor = new Color(0.22f, 0.26f, 0.34f, 0.95f);
            s.ButtonSelectedColor = new Color(0.25f, 0.55f, 0.95f, 1f);
            s.PrimaryButtonColor = new Color(0.30f, 0.75f, 0.40f, 1f);
            s.MenuButtonSize = new Vector2(230f, 100f);
            s.PlayButtonSize = new Vector2(480f, 140f);
            s.MenuRowSpacing = 36f;
            s.MenuItemSpacing = 18f;
            s.VersionMargin = new Vector2(24f, 24f);
            s.ScoreBarTopMargin = 20f;
            s.ScoreBoxSize = new Vector2(150f, 90f);
            s.TimerBoxSize = new Vector2(200f, 90f);
            s.ScoreBarSpacing = 12f;
            s.ScorePanelColor = new Color(0f, 0f, 0f, 0.45f);
            s.CountdownSize = new Vector2(360f, 70f);
            s.ResultsBackdropColor = new Color(0.05f, 0.06f, 0.09f, 0.88f);
            s.MvpCapsuleSize = new Vector2(110f, 200f);
            s.MvpBounceHeight = 40f;
            s.MvpBounceFrequency = 1.5f;
            s.ResultsRowHeight = 52f;
            s.ResultsNameColumnWidth = 360f;
            s.ResultsStatColumnWidth = 210f;
            s.ResultsSpacing = 24f;
            s.ResultsButtonSize = new Vector2(400f, 120f);

            // Online (S3.11, PROTO sizes at 1920×1080 — check on the phone): ping badge under the FPS counter,
            // connection overlay below the score bar, matchmaking backdrop over the menu.
            s.PingGoodColor = new Color(0.35f, 0.9f, 0.4f, 1f);
            s.PingFairColor = new Color(1f, 0.85f, 0.25f, 1f);
            s.PingPoorColor = new Color(1f, 0.35f, 0.3f, 1f);
            s.PingUnknownColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            s.PingMargin = new Vector2(24f, 8f);
            s.PingBoxSize = new Vector2(220f, 44f);
            s.OverlayPanelColor = new Color(0f, 0f, 0f, 0.6f);
            s.OverlayBoxSize = new Vector2(1100f, 90f);
            s.OverlayTopMargin = 200f;
            s.MatchmakingBackdropColor = new Color(0.05f, 0.06f, 0.09f, 0.94f);
            EditorUtility.SetDirty(s);
        }

        private static TelegraphView BuildTelegraphPrefab(Material zone, Material fill)
        {
            var root = new GameObject("Telegraph");
            try
            {
                // Unity cylinder: height 2, diameter 1 — flat discs of diameter 1 (root X/Z scale = zone diameter).
                GameObject zoneGo = CreatePart(PrimitiveType.Cylinder, "Zone", root.transform, zone);
                zoneGo.transform.localScale = new Vector3(1f, TelegraphThickness * 0.5f, 1f);
                zoneGo.transform.localPosition = new Vector3(0f, TelegraphZoneHeight, 0f);
                GameObject fillGo = CreatePart(PrimitiveType.Cylinder, "Fill", root.transform, fill);
                fillGo.transform.localScale = new Vector3(0f, TelegraphThickness * 0.5f, 0f);
                fillGo.transform.localPosition = new Vector3(0f, TelegraphFillHeight, 0f);

                var view = root.AddComponent<TelegraphView>();
                var so = new SerializedObject(view);
                SetRef(so, "_zone", zoneGo.GetComponent<Renderer>());
                SetRef(so, "_fill", fillGo.transform);
                SetRef(so, "_fillRenderer", fillGo.GetComponent<Renderer>());
                so.ApplyModifiedPropertiesWithoutUndo();
                return SavePrefab(root, TelegraphPrefabPath).GetComponent<TelegraphView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Color WithAlpha(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        /// <summary>Radial white texture with a soft alpha edge (tinted by the material colour), saved as PNG and imported.</summary>
        private static Texture2D BuildBlobShadowTexture()
        {
            int n = BlobShadowTextureSize;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[n * n];
                float half = n * 0.5f;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float t = Mathf.Clamp01((r - BlobShadowSoftStart) / (1f - BlobShadowSoftStart));
                        float a = r >= 1f ? 0f : 1f - t * t * (3f - 2f * t);
                        pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                System.IO.File.WriteAllBytes(BlobShadowTexturePath, tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(BlobShadowTexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(BlobShadowTexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(BlobShadowTexturePath);
        }

        private static FighterView BuildFighterViewPrefab(Material body, Material ring, Material nose, Material shadow)
        {
            var root = new GameObject("FighterView");
            try
            {
                // Blob shadow (D-027): Unity quad faces −Z, turned to lie on the ground.
                GameObject shadowGo = CreatePart(PrimitiveType.Quad, "Shadow", root.transform, shadow);
                shadowGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shadowGo.transform.localScale = new Vector3(BlobShadowDiameter, BlobShadowDiameter, 1f);
                shadowGo.transform.localPosition = new Vector3(0f, BlobShadowHeight, 0f);

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
                SetRef(so, "_shadow", shadowGo.GetComponent<Renderer>());
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

        private static GameObject BuildSpherePrefab(string name, string path, Material material, float diameter)
        {
            var root = new GameObject(name);
            try
            {
                GameObject sphere = CreatePart(PrimitiveType.Sphere, "Sphere", root.transform, material);
                sphere.transform.localScale = new Vector3(diameter, diameter * 1.2f, diameter); // slightly egg-shaped
                return SavePrefab(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildHollowPrefab(Material material)
        {
            var root = new GameObject("Hollow");
            try
            {
                // Unity cylinder: height 2, diameter 1.
                GameObject stump = CreatePart(PrimitiveType.Cylinder, "Stump", root.transform, material);
                stump.transform.localScale = new Vector3(HollowDiameter, HollowHeight * 0.5f, HollowDiameter);
                stump.transform.localPosition = new Vector3(0f, HollowHeight * 0.5f, 0f);
                return SavePrefab(root, HollowPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildLogPrefab(Material material)
        {
            var root = new GameObject("Log");
            try
            {
                // Unity cylinder: height 2 along Y, diameter 1 → turned to lie along X, length 1 (root scale = length, thickness).
                GameObject trunk = CreatePart(PrimitiveType.Cylinder, "Trunk", root.transform, material);
                trunk.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                trunk.transform.localScale = new Vector3(1f, 0.5f, 1f);
                return SavePrefab(root, LogPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildMinePrefab(Material material)
        {
            var root = new GameObject("Mine");
            try
            {
                GameObject disc = CreatePart(PrimitiveType.Cylinder, "Cone", root.transform, material);
                disc.transform.localScale = new Vector3(MineDiameter, MineHeight * 0.5f, MineDiameter);
                disc.transform.localPosition = new Vector3(0f, MineHeight * 0.5f, 0f);
                return SavePrefab(root, MinePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildDomePrefab(Material material)
        {
            var root = new GameObject("Dome");
            try
            {
                // Unity sphere of diameter 1 centred on the ground: the upper half is the dome (root scale = area diameter).
                CreatePart(PrimitiveType.Sphere, "Shell", root.transform, material);
                return SavePrefab(root, DomePrefabPath);
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
