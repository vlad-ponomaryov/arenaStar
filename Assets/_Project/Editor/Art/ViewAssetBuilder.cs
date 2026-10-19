using System;
using Arena.Editor.Art.MapProps;
using Arena.Editor.Art.Fighters;
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
        public const string BerryBoxPrefabPath = PrefabsFolder + "/BerryBox.prefab";
        public const string BerryPrefabPath = PrefabsFolder + "/Berry.prefab";
        public const string FogPrefabPath = PrefabsFolder + "/Fog.prefab";
        public const string MeshesFolder = ArtRoot + "/Meshes";
        public const string FallbackCapsuleMeshPath = MeshesFolder + "/FallbackCapsule.asset";

        private const string LitShader = "Universal Render Pipeline/Lit";
        public const string ToonCharacterShader = "Arena/ToonCharacter";
        public const string ToonCharacterGhostShader = "Arena/ToonCharacterGhost";
        public const string ToonEnvShader = "Arena/ToonEnv";
        public const string GrassTexturePath = FlowImageImporter.TargetFolder + "/Grass.png";
        private const string FlashProperty = "_Flash";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";
        private const string BaseColorProperty = "_BaseColor";
        private const string BaseMapProperty = "_BaseMap";
        private const string MainTexProperty = "_MainTex";
        private const string CircleSpritePath = "UI/Skin/Knob.psd";
        public const string FontsFolder = ArtRoot + "/Fonts";
        public const string BodyFontPath = FontsFolder + "/Nunito-Bold.ttf";
        public const string HeadingFontPath = FontsFolder + "/Nunito-ExtraBold.ttf";

        // Team palette (D-029): blue allies, red enemies with a clear brightness difference.
        private static readonly Color AllyColor = new Color(0.20f, 0.52f, 1.00f, 1f);
        private static readonly Color EnemyColor = new Color(0.95f, 0.22f, 0.18f, 1f);
        // Fighter materials are white: models carry their colours in vertex colours (Art-Spec §6.2); the fallback capsule bakes
        // its beige into its vertices (FallbackCapsuleColor).
        private static readonly Color BodyColor = Color.white;
        private static readonly Color FallbackCapsuleColor = new Color(0.86f, 0.80f, 0.70f, 1f);

        // Colour-blind palette (D-029): allies stay blue, enemies orange (blue/orange is told apart by all common types).
        private static readonly Color EnemyAltColor = new Color(1.00f, 0.60f, 0.10f, 1f);

        // Feel (S2.10, D-059): hit flash = the toon body with _Flash = 1 (white, outline kept, Art-Spec §6.3); telegraph =
        // translucent zone + stronger growing fill by team.
        private const float TelegraphZoneAlpha = 0.18f;
        private const float TelegraphFillAlpha = 0.42f;
        private const float TelegraphThickness = 0.01f;
        private const float TelegraphZoneHeight = 0.03f;
        private const float TelegraphFillHeight = 0.04f;
        private static readonly Color AimLineColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color ProjectileColor = new Color(0.55f, 0.88f, 1f, 1f);
        private static readonly Color BarChargeColor = new Color(1f, 0.78f, 0.2f, 1f);

        // Map (S2.5): walls grey-brown, breakable walls lighter wood, bushes dark green, water blue; translucent variants for
        // hiding (D-014, D-024).
        // A.3b: the crate, the bush and the water carry their palette in vertex colours / shader properties; these tint them.
        private static readonly Color WallBreakableColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        private static readonly Color BushColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color BushSeeThroughColor = new Color(1f, 1f, 1f, 0.38f);
        private static readonly Color WaterColor = Color.white;
        private static readonly Color FighterHiddenColor = new Color(1f, 1f, 1f, 0.45f);

        // Map geometry (world units, view only, PROTO): wall and breakable wall height, bush height, water sheet height,
        // see-through radius around the local fighter in a bush.
        private const float WallHeight = 0.9f;      // A.3f: separate stone blocks (0.86 + leaf relief), crate 0.8
        private const float WaterHeight = -0.1f;    // A.3b: a sunken pond with plank banks
        private const float BankOverlap = 0.03f;
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

        // Last Beast mode (S3.14, view only, PROTO): a crate slightly lower than a wall, a small bobbing red berry, purple fog
        // a little above the ground; the fog edge reaches a new step in ≈ 0.5 s.
        private static readonly Color BerryBoxColor = new Color(0.95f, 0.95f, 0.95f, 1f); // A.3b: palette in the basket vertex colours
        private static readonly Color BerryColor = new Color(0.85f, 0.12f, 0.30f, 1f);
        private static readonly Color FogColor = new Color(0.45f, 0.25f, 0.60f, 0.45f);
        private const float BerryDiameter = 0.35f;
        private const float BerryHeight = 0.35f;
        private const float BerryBobHeight = 0.06f;
        private const float BerryBobSpeed = 3f;
        private const int BerryPoolPrewarm = 16;
        private const float FogHeight = 0.06f;
        private const float FogFollowSharpness = 6f;

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

        // Health bar (world units, A.7a): a rounded "pill" frame (texture, its corner radius keeps the square fill inside the
        // outline), the fill with a gloss strip, round charge beads under it; drawn layers slightly towards the camera.
        private const float BarAboveFeet = 2.1f;
        private const float BarAboveTop = 0.62f;
        private const float BarWidth = 1.1f;
        private const float BarHeight = 0.15f;
        private const float BarPadding = 0.04f;
        private const float BarGlossShare = 0.4f;
        private const float ChargeBead = 0.15f;
        private const float ChargeBeadSpacing = 0.2f;
        private const float ChargeGap = 0.03f;
        private const float OwnBeadScale = 1.3f;
        private const float LayerOffset = 0.005f;
        private const int ChargeSegments = 3;
        private const int BarTexturePixelsPerUnit = 400;
        private const float BarFrameRadius = 0.08f;
        private const float BarFrameOutline = 0.016f;
        private const int BeadTextureSize = 64;
        private static readonly Color BarFrameInside = new Color(0.231f, 0.165f, 0.118f, 0.55f);
        private static readonly Color BarGlossColor = new Color(1f, 1f, 1f, 0.28f);
        private static readonly Color BarSocketColor = new Color(0.231f, 0.165f, 0.118f, 0.55f);

        // Team ring (A.7a): flat quad, ring band with a light inner rim and a faint disc inside (texture), team colour lightened.
        private const int RingTextureSize = 128;
        private const float RingBandInner = 0.74f;
        private const float RingRimInner = 0.70f;
        private const float RingDiscAlpha = 0.14f;
        private const float RingBandShade = 0.78f;
        private const float RingLighten = 0.2f;
        public const string BarFrameTexturePath = TexturesFolder + "/HudBarFrame.png";
        public const string BeadTexturePath = TexturesFolder + "/HudBead.png";
        public const string RingTexturePath = TexturesFolder + "/HudRing.png";

        // Fighter view geometry (cells): capsule of diameter 0.9 (≈ body radius 0.45), ring slightly larger.
        private const float BodyDiameter = 0.9f;
        private const float BodyHeight = 1.6f;
        private const float RingDiameter = 1.3f;
        private const float RingHeight = 0.02f;
        private static readonly Vector3 NoseScale = new Vector3(0.22f, 0.22f, 0.3f);
        private const float NoseForward = 0.45f;
        private const float NoseHeightFraction = 0.75f;

        // Fallback capsule (A.1a): a copy of Unity's capsule with vertex colours (meshes under Arena/* shaders need them, Art-Spec
        // §2.1): linear beige albedo, alpha = gloss of the toon highlight (a vinyl toy, a little shiny).
        private const byte FallbackCapsuleGloss = 140;

        // Light (Art-Spec §6.3): pitch 50°, yaw = camera yaw + 35° (MatchLightRig).
        private const float LightPitch = 50f;
        private const float LightYawFromCamera = 35f;

        // Blob shadow under a fighter (D-027, S2.13): code-generated radial texture on a flat quad just above the floor.
        private const int BlobShadowTextureSize = 64;
        private const float BlobShadowSoftStart = 0.55f; // fraction of the radius where the edge starts to fade
        private const float BlobShadowDiameter = 1.15f;
        private const float BlobShadowHeight = 0.01f;
        private static readonly Color BlobShadowColor = new Color32(0x22, 0x30, 0x1E, 0x66); // dark green α 0.4 (Art-Spec §1.3)

        // Environment (A.3a, Art-Spec §1.3, §2.1): Arena/ToonEnv; floor and meadow = Flow grass 002 in world UV (D-123), walls = vertex
        // colours only. Grass texture: seam feathering, 1024 → 512, mean moved towards the floor palette (#A8CF86 / #9AC377).
        private const string GrassSource = FlowImageImporter.SourceRoot + "/2026-10-19_tex-grass/002.png";
        private const int GrassTextureSize = 512;
        private const int GrassSeamFeatherPx = 24;
        private const float GrassMaxGain = 1.15f;
        private static readonly Color GrassTargetMean = new Color32(0xA3, 0xCB, 0x81, 0xFF);
        private const float FloorWorldTiling = 7f;    // cells per grass tile: soft patches every ≈ 3–4 cells
        private const float FloorMacroTiling = 23f;   // the larger turned sample that breaks the repetition
        private const float FloorMacroStrength = 0.3f;
        // A.3d (D-128): the arena floor is calm baked sand (EnvTextureBaker.BakeSand: slabs of 2 × 2 cells, mottling, rare cracks);
        // the Flow pebble floor of A.3c (floor_001) stays only in Art/Source — painted pebbles everywhere read as noise. Grass patches
        // (uv0.y of the floor mesh, MapMeshStyle.FloorPatch*) only as a faint fringe at bushes, drawn with the grass map × PatchTint.
        public const string FloorSandTexturePath = EnvTextureBaker.SandPath;
        private const string OldFloorSandTexturePath = FlowImageImporter.TargetFolder + "/FloorSand.png";
        private const float FloorSandTiling = EnvTextureBaker.SandCellsPerTile;
        private const float FloorSandContrast = 1f;
        private const float FloorSandMacroStrength = 0.15f;
        private static readonly Color FloorTint = new Color(0.95f, 1f, 1.1f, 1f);   // lit sand -> concept #F9D28A
        private static readonly Color PatchTint = new Color(0.86f, 0.95f, 0.62f, 1f);
        private const float PatchTiling = 4f;
        private const float PatchEdge = 0.05f;
        private const float PatchTexBreakup = 1.2f;
        private static readonly Color ObstaclePropTint = new Color(0.97f, 0.97f, 0.97f, 1f);
        private const int ObstaclePropMaxCells = 3;
        private const int ObstacleStoneEvery = 4;
        private const string PatchMapProperty = "_PatchMap";
        private const string PatchMeanProperty = "_PatchMeanLuminance";
        private const string PatchTilingProperty = "_PatchTiling";
        private const string PatchTintProperty = "_PatchTint";
        private const string PatchEdgeProperty = "_PatchEdge";
        private const string PatchBreakupProperty = "_PatchBreakup";
        private static readonly Color MeadowTint = new Color(0.64f, 0.76f, 0.62f, 1f);  // grass × this ≈ #6E9E58 (darker meadow)
        private const float MeadowContrast = 0.5f;   // the meadow is calmer than the arena floor: the eye stays on the play field
        private static readonly Color WallMaterialTint = new Color(0.93f, 0.93f, 0.93f, 1f); // light + ambient brighten the cream top
        private static readonly MapMeshStyle MapStyle = new MapMeshStyle
        {
            WallBevel = 0.08f,
            WallTop = new Color32(0xF3, 0xE3, 0xC3, 0xFF),
            WallSide = new Color32(0x8C, 0x54, 0x22, 0xFF),   // A.3d: lit side → concept #A84A14 (dark warm side under a light top)
            WallBevelHighlight = 0.45f,
            WallFootShade = 0.72f,
            WallCellVariation = 0.04f,
            FloorSubdivisions = 2,
            FloorWallOcclusion = 0.2f,   // A.3e: the contact shade is the mask's AO channel now; vertices keep a faint broad term
            FloorOcclusionRadius = 0.75f,
            FloorOcclusionColor = new Color32(0xB0, 0x78, 0x52, 0xFF),   // A.3e: warm clay shade (was lavender)
            FloorBushOcclusion = 0.15f,
            FloorBushOcclusionRadius = 0.45f,
            FloorPatchCell = 2.2f,
            FloorPatchCoverage = 0f,     // A.3d: no grass stains on the sand (they read as mould)
            FloorPatchNearBush = 0.35f,  // a faint fringe at bushes
            ObstaclePropRadius = 0.4f,
            MeadowMargin = 14f,
            MeadowFade = 6f,
            MeadowEdge = new Color32(0xBC, 0xC4, 0xCC, 0xFF),
            MeadowInner = new Color32(0xC8, 0xD2, 0xC8, 0xFF),
            MeadowInnerWidth = 0.6f,
            WaterSubdivisions = 4,
            WaterShoreWidth = 0.42f,
            BankTop = new Color32(0xB9, 0x8A, 0x5E, 0xFF),
            BankFoot = new Color32(0x6E, 0x4E, 0x34, 0xFF),
            PondRimWidth = 0.15f,        // A.3f: a round pond with a plank rim (concept 002), corners by the neighbour mask
            PondRimHeight = 0.07f,
            PondCornerRadius = 0.9f,
            PondPlankLength = 0.17f,
            PondPlankLight = new Color32(0xC8, 0x82, 0x48, 0xFF),
            PondPlankDark = new Color32(0xB0, 0x6C, 0x3A, 0xFF),
            PondPlankSeam = new Color32(0x6A, 0x40, 0x22, 0xFF),
            PondRimSide = new Color32(0x9A, 0x5E, 0x32, 0xFF),
            PondShoreMax = 0.6f,
            WallBlocksSeparate = true,
            ShadowTexelsPerCell = 8,     // A.3e (D-130): 8 texels per cell, blurred footprints, G = contact AO
            ShadowStrength = 0.9f,
            ShadowSoftness = 0.15f,
            ShadowFall = new Vector2(0.55f, 0.4f),
            BushShadowHeight = 0.55f,
            CrateShadowHeight = 0.8f,
            BoxShadowHeight = 0.5f,
            PropShadowHeight = 0.65f,
            FootprintInset = 0.06f,
            FootprintCorner = 0.15f,
            ShadowBlur = 0.14f,          // A.3f: lower separate blocks — a slightly softer edge
            ContactAoBlur = 0.24f,
            ContactAoStrength = 0.85f,
            BushAoWeight = 0.6f,
            Grade = new Vector3(0.05f, 0.02f, 0.03f)
        };

        // A.3e (D-130): floor details as geometry (FloorDecorPlanner) — stepping-stone paths base → centre and across, rare
        // pebbles, sprouts, flowers (concept 002: ≈ 1 small detail per 3–4 free cells).
        private static EnvDecorStyle DecorStyle() => new EnvDecorStyle
        {
            Paths = new[] { new Vector4(0.5f, 0.04f, 0.5f, 0.5f), new Vector4(0.1f, 0.3f, 0.9f, 0.3f) },
            PathStep = 0.58f,
            PathHalfWidth = 0.27f,
            PathJitter = 0.07f,
            PathPairChance = 0.6f,
            StoneRadius = new Vector2(0.2f, 0.3f),
            StoneAspect = new Vector2(0.65f, 0.9f),
            StoneHeight = 0.05f,
            StoneTop = new Color32(0xF6, 0xDF, 0xB0, 0xFF),
            StoneEdge = new Color32(0xC4, 0x9A, 0x6C, 0xFF),
            ClearMargin = 0.06f,
            PebbleChance = 0.08f,
            SproutChance = 0.1f,
            FlowerChance = 0.05f,
            PebbleRadius = new Vector2(0.04f, 0.07f),
            PebbleTop = new Color32(0xC9, 0xA2, 0x7A, 0xFF),
            PebbleEdge = new Color32(0x8E, 0x6A, 0x4A, 0xFF),
            SproutSize = 0.14f,
            SproutBase = new Color32(0x6D, 0x8F, 0x2A, 0xFF),
            SproutTip = new Color32(0xB8, 0xD8, 0x5A, 0xFF),
            FlowerSize = 0.08f,
            FlowerPetals = new Color[] { new Color32(0xFF, 0x8F, 0xB4, 0xFF), new Color32(0xFF, 0xF4, 0xDE, 0xFF), new Color32(0xFF, 0xD5, 0x4F, 0xFF) },
            FlowerCentre = new Color32(0xF2, 0xA2, 0x3A, 0xFF),
            StoneClearance = 0.35f,
            MaxTriangles = 5000
        };

        // A.3b environment (D-125). Wall: top = baked stone slabs with moss (2 cells per tile, aligned to the cells), sides = Flow
        // planks 001 (horizontal boards, 2.4 units per tile = 8 boards); both divided by their mean (the vertex colours keep the
        // palette). Crate = Flow planks 002, basket = Flow wicker 001. Bush = leaf detail, water = ripple map (EnvTextureBaker).
        private const string WallSidePlanksSource = FlowImageImporter.SourceRoot + "/2026-10-19_tex-crate-planks/001.png";
        private const string CratePlanksSource = FlowImageImporter.SourceRoot + "/2026-10-19_tex-crate-planks/002.png";
        private const string WickerSource = FlowImageImporter.SourceRoot + "/2026-10-19_tex-wicker/001.png";
        public const string WallPlanksTexturePath = FlowImageImporter.TargetFolder + "/WallPlanks.png";
        public const string CratePlanksTexturePath = FlowImageImporter.TargetFolder + "/CratePlanks.png";
        public const string WickerTexturePath = FlowImageImporter.TargetFolder + "/Wicker.png";
        public const string EnvMeshesFolder = MeshesFolder + "/Env";
        private const int PropTextureSize = 512;
        private const int PropSeamFeatherPx = 16;
        private const float PropMaxGain = 1.1f;
        private static readonly Color PlanksTargetMean = new Color32(0xD8, 0x9A, 0x58, 0xFF);
        private static readonly Color WickerTargetMean = new Color32(0xD9, 0xA5, 0x5B, 0xFF);
        private const float WallTopTiling = EnvTextureBaker.WallTopCellsPerTile;
        private const float WallSideTiling = 2.4f;
        private const float WallSideContrast = 0.85f;
        private const float CrateTopTiling = 1.6f;
        private const float CrateSideTiling = 1.6f;
        private const float CrateContrast = 0.9f;
        private const float BasketTiling = 0.7f;
        private const float BasketContrast = 0.9f;
        private const float BasketSpecStrength = 0.6f;
        private const float BasketSpecSize = 0.95f;
        private const float LeafTiling = 0.9f;
        private const float LeafContrast = 0f;   // A.3d: the blades are geometry, no painted leaves
        private const float RippleTiling = 2.2f;
        private const float SeeThroughSrcBlend = (float)BlendMode.SrcAlpha;
        private const float SeeThroughDstBlend = (float)BlendMode.OneMinusSrcAlpha;
        public const string BushShader = "Arena/Bush";
        public const string WaterShader = "Arena/Water";
        private const string WorldTilingProperty = "_WorldTiling";
        private const string MacroTilingProperty = "_MacroTiling";
        private const string MacroStrengthProperty = "_MacroStrength";
        private const string MapMeanLuminanceProperty = "_MapMeanLuminance";
        private const string MapContrastProperty = "_MapContrast";
        private const string BaseNormalizeProperty = "_BaseNormalize";
        private const string SideMapProperty = "_SideMap";
        private const string SideMeanProperty = "_SideMeanLuminance";
        private const string SideNormalizeProperty = "_SideNormalize";
        private const string SideTilingProperty = "_SideTiling";
        private const string SideContrastProperty = "_SideContrast";
        private const string ReceiveMapShadowProperty = "_ReceiveMapShadow";
        private const string SpecStrengthProperty = "_SpecStrength";
        private const string SpecSizeProperty = "_SpecSize";
        private const string LeafMapProperty = "_LeafMap";
        private const string LeafMeanProperty = "_LeafMeanLuminance";
        private const string LeafTilingProperty = "_LeafTiling";
        private const string LeafContrastProperty = "_LeafContrast";
        private const string RippleMapProperty = "_RippleMap";
        private const string RippleTilingProperty = "_RippleTiling";

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
            ProjectConfigurator.EnsureFolder(MeshesFolder);

            Texture2D ringTexture = BuildHudTexture(RingTexturePath, RingTextureSize, RingTextureSize, RingPixel);
            Material ally = SaveTexturedMaterial("TeamAlly", Lighten(AllyColor), ringTexture);
            Material enemy = SaveTexturedMaterial("TeamEnemy", Lighten(EnemyColor), ringTexture);
            Material body = SaveToonMaterial("FighterBody", ToonCharacterShader, BodyColor, 0f);
            (Texture2D grass, float grassMean) = FlowImageImporter.ImportTileable(GrassSource, GrassTexturePath, GrassTextureSize,
                GrassTargetMean, GrassMaxGain, GrassSeamFeatherPx);
            AssetDatabase.DeleteAsset(OldFloorSandTexturePath);
            (Texture2D sand, float sandMean) = EnvTextureBaker.BakeSand();
            Material floor = SaveEnvMaterial("Floor", FloorTint, sand, sandMean, FloorSandContrast);
            floor.SetFloat(WorldTilingProperty, FloorSandTiling);
            floor.SetFloat(MacroStrengthProperty, FloorSandMacroStrength);
            floor.SetTexture(PatchMapProperty, grass);
            floor.SetFloat(PatchMeanProperty, Mathf.Max(0.05f, grassMean));
            floor.SetFloat(PatchTilingProperty, PatchTiling);
            floor.SetColor(PatchTintProperty, PatchTint);
            floor.SetFloat(PatchEdgeProperty, PatchEdge);
            floor.SetFloat(PatchBreakupProperty, PatchTexBreakup);
            Material meadow = SaveEnvMaterial("Meadow", MeadowTint, grass, grassMean, MeadowContrast);
            floor.SetFloat(ReceiveMapShadowProperty, 1f);
            meadow.SetFloat(ReceiveMapShadowProperty, 1f);
            Material aimLine = SaveMaterial("AimLine", UnlitShader, AimLineColor);
            Material projectile = SaveMaterial("Projectile", UnlitShader, ProjectileColor);
            Material barAlly = SaveMaterial("BarAlly", UnlitShader, AllyColor);
            Material barEnemy = SaveMaterial("BarEnemy", UnlitShader, EnemyColor);
            Texture2D barFrame = BuildHudTexture(BarFrameTexturePath, BarFrameTextureWidth, BarFrameTextureHeight, BarFramePixel);
            Texture2D bead = BuildHudTexture(BeadTexturePath, BeadTextureSize, BeadTextureSize, BeadPixel);
            Material barBackground = SaveTexturedMaterial("BarBackground", Color.white, barFrame);
            Material barCharge = SaveTexturedMaterial("BarCharge", BarChargeColor, bead);
            Material barSocket = SaveTexturedMaterial("BarChargeSocket", BarSocketColor, bead);
            Material barGloss = SaveMaterial("BarGloss", UnlitShader, BarGlossColor, true);
            (Texture2D wallTop, float wallTopMean) = EnvTextureBaker.BakeWallTop();
            (Texture2D wallPlanks, float wallPlanksMean) = FlowImageImporter.ImportTileable(WallSidePlanksSource, WallPlanksTexturePath,
                PropTextureSize, PlanksTargetMean, PropMaxGain, PropSeamFeatherPx);
            (Texture2D cratePlanks, float cratePlanksMean) = FlowImageImporter.ImportTileable(CratePlanksSource, CratePlanksTexturePath,
                PropTextureSize, PlanksTargetMean, PropMaxGain, PropSeamFeatherPx);
            (Texture2D wicker, float wickerMean) = FlowImageImporter.ImportTileable(WickerSource, WickerTexturePath, PropTextureSize,
                WickerTargetMean, PropMaxGain, PropSeamFeatherPx);
            (Texture2D leaves, float leavesMean) = EnvTextureBaker.BakeLeaves();
            (Texture2D ripple, _) = EnvTextureBaker.BakeRipple();
            Material wall = SavePropMaterial("Wall", WallMaterialTint, wallTop, wallTopMean, WallTopTiling, wallPlanks, wallPlanksMean,
                WallSideTiling, WallSideContrast);
            Material wallBreakable = SavePropMaterial("WallBreakable", WallBreakableColor, cratePlanks, cratePlanksMean, CrateTopTiling,
                cratePlanks, cratePlanksMean, CrateSideTiling, CrateContrast);
            Material bush = SaveBushMaterial("Bush", BushColor, leaves, leavesMean, false);
            Material bushSeeThrough = SaveBushMaterial("BushSeeThrough", BushSeeThroughColor, leaves, leavesMean, true);
            Material water = SaveToonMaterial("Water", WaterShader, WaterColor, 0f);
            water.SetTexture(RippleMapProperty, ripple);
            water.SetFloat(RippleTilingProperty, RippleTiling);
            EditorUtility.SetDirty(water);
            Mesh[] bushMeshes = SaveEnvMeshes();
            Mesh crateMesh = SaveMesh(EnvMeshBuilder.BuildCrate(), EnvMeshesFolder + "/Crate.asset");
            Mesh basketMesh = SaveMesh(EnvMeshBuilder.BuildBasket(), EnvMeshesFolder + "/BerryBasket.asset");
            Mesh[] wallBlocks = EnvMeshBuilder.BuildWallBlocks();
            for (int i = 0; i < wallBlocks.Length; i++)
                wallBlocks[i] = SaveMesh(wallBlocks[i], EnvMeshesFolder + "/" + wallBlocks[i].name + ".asset");
            Mesh[] obstacleProps = EnvMeshBuilder.BuildObstacleProps();
            for (int i = 0; i < obstacleProps.Length; i++)
            {
                string propName = obstacleProps[i].name;
                obstacleProps[i] = SaveMesh(obstacleProps[i], EnvMeshesFolder + "/" + propName + ".asset");
            }
            Material obstacleProp = SaveToonMaterial("ObstacleProp", ToonEnvShader, ObstaclePropTint, 0f);
            Material fighterHidden = SaveToonMaterial("FighterHidden", ToonCharacterGhostShader, FighterHiddenColor, 0f);
            Material enemyAlt = SaveTexturedMaterial("TeamEnemyAlt", Lighten(EnemyAltColor), ringTexture);
            Material barEnemyAlt = SaveMaterial("BarEnemyAlt", UnlitShader, EnemyAltColor);
            Material flash = SaveToonMaterial("FighterFlash", ToonCharacterShader, BodyColor, 1f);
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
            Mesh fallbackCapsule = BuildFallbackCapsuleMesh();
            FighterView fighterView = BuildFighterViewPrefab(body, ally, aimLine, blobShadow, fallbackCapsule);
            AimIndicatorView aimIndicator = BuildAimIndicatorPrefab(aimLine);
            GameObject floorPrefab = BuildMeadowPrefab(meadow);
            GameObject projectilePrefab = BuildProjectilePrefab(projectile);
            HealthBarView healthBar = BuildHealthBarPrefab(barAlly, barBackground, barCharge, barSocket, barGloss);
            GameObject breakablePrefab = BuildMeshPrefab("BreakableWall", BreakableWallPrefabPath, crateMesh, wallBreakable);
            GameObject bushPrefab = BuildMeshPrefab("Bush", BushPrefabPath, bushMeshes[0], bush);
            Material acorn = SaveMaterial("Acorn", LitShader, AcornColor);
            Material acornGolden = SaveMaterial("AcornGolden", UnlitShader, AcornGoldenColor);
            Material hollow = SaveMaterial("Hollow", LitShader, HollowColor);
            GameObject acornPrefab = BuildSpherePrefab("Acorn", AcornPrefabPath, acorn, AcornDiameter);
            GameObject acornGoldenPrefab = BuildSpherePrefab("AcornGolden", AcornGoldenPrefabPath, acornGolden, AcornGoldenDiameter);
            GameObject hollowPrefab = BuildHollowPrefab(hollow);
            Material berryBox = SavePropMaterial("BerryBox", BerryBoxColor, wicker, wickerMean, BasketTiling, wicker, wickerMean,
                BasketTiling, BasketContrast);
            berryBox.SetFloat(SpecStrengthProperty, BasketSpecStrength);
            berryBox.SetFloat(SpecSizeProperty, BasketSpecSize);
            Material berry = SaveMaterial("Berry", UnlitShader, BerryColor);
            Material fog = SaveMaterial("Fog", UnlitShader, FogColor, true);
            GameObject berryBoxPrefab = BuildMeshPrefab("BerryBox", BerryBoxPrefabPath, basketMesh, berryBox);
            GameObject berryPrefab = BuildSpherePrefab("Berry", BerryPrefabPath, berry, BerryDiameter);
            GameObject fogPrefab = BuildQuadPrefab("Fog", FogPrefabPath, fog);
            Material log = SaveMaterial("Log", LitShader, LogColor);
            Material pineCone = SaveMaterial("PineCone", LitShader, PineConeColor);
            Material mine = SaveMaterial("Mine", LitShader, MineColor);
            GameObject logPrefab = BuildLogPrefab(log);
            GameObject lobBombPrefab = BuildSpherePrefab("LobBomb", LobBombPrefabPath, pineCone, LobBombDiameter);
            GameObject minePrefab = BuildMinePrefab(mine);
            GameObject domePrefab = BuildDomePrefab(telegraphAllyZone);

            ViewCatalog catalog = LoadOrCreate<ViewCatalog>(ViewCatalogPath);
            catalog.FighterViewPrefab = fighterView;
            // Per-fighter procedural models (A.1b: Pix; A.1c–A.1d: the rest); fighters without one use the fallback capsule.
            catalog.FighterVisuals = FighterModelBuilder.BuildAll(body, ally, blobShadow);
            catalog.LightPitch = LightPitch;
            catalog.LightYawFromCamera = LightYawFromCamera;
            catalog.AimIndicatorPrefab = aimIndicator;
            catalog.FloorPrefab = floorPrefab;
            catalog.ProjectilePrefab = projectilePrefab;
            catalog.HealthBarPrefab = healthBar;
            catalog.BreakableWallPrefab = breakablePrefab;
            catalog.BushPrefab = bushPrefab;
            catalog.BushVariants = bushMeshes;
            catalog.BushVariantsPerMask = EnvMeshBuilder.BushVariantCount;
            catalog.ObstacleProps = obstacleProps;
            catalog.WallBlockVariants = wallBlocks;
            catalog.ObstacleProp = obstacleProp;
            catalog.ObstaclePropMaxCells = ObstaclePropMaxCells;
            catalog.ObstacleStoneEvery = ObstacleStoneEvery;
            catalog.BankOverlap = BankOverlap;
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
            catalog.BerryBoxPrefab = berryBoxPrefab;
            catalog.BerryPrefab = berryPrefab;
            catalog.FogPrefab = fogPrefab;
            catalog.BerryPoolPrewarm = BerryPoolPrewarm;
            catalog.BerryHeight = BerryHeight;
            catalog.BerryBobHeight = BerryBobHeight;
            catalog.BerryBobSpeed = BerryBobSpeed;
            catalog.FogHeight = FogHeight;
            catalog.FogFollowSharpness = FogFollowSharpness;
            catalog.BerryBox = berryBox;
            catalog.Berry = berry;
            catalog.Fog = fog;
            catalog.WallHeight = WallHeight;
            catalog.WaterHeight = WaterHeight;
            catalog.BushSeeThroughRadius = BushSeeThroughRadius;
            catalog.Wall = wall;
            catalog.WallBreakable = wallBreakable;
            catalog.Bush = bush;
            catalog.BushSeeThrough = bushSeeThrough;
            catalog.Water = water;
            catalog.Meadow = meadow;
            catalog.MapStyle = MapStyle;
            catalog.DecorStyle = DecorStyle();
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

            SeedUi(catalog);

            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_VIEW_ASSETS_BUILT");
        }

        /// <summary>
        /// Re-seeds only the UI style, its baked sprites and the text font of the catalog (A.6) — quick UI iterations without
        /// rebuilding the models and the map. Run: Tools/unity-method.ps1 -Method Arena.Editor.Art.ViewAssetBuilder.BuildUi
        /// </summary>
        public static void BuildUi()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewCatalogPath);
            if (catalog == null) throw new InvalidOperationException("ViewAssetBuilder: run BuildAll first, no " + ViewCatalogPath);
            SeedUi(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_UI_ASSETS_BUILT");
        }

        private static void SeedUi(ViewCatalog catalog)
        {
            UiStyle uiStyle = LoadOrCreate<UiStyle>(UiStylePath);
            SeedUiStyle(uiStyle);
            // Floating numbers use the UI heading font and outline (Arena.Gameplay does not see UiStyle, D-049).
            catalog.TextFont = uiStyle.HeadingFont;
            catalog.TextOutlineColor = uiStyle.TextOutlineColor;
            catalog.TextOutlineDistance = uiStyle.TextOutlineDistance;
            EditorUtility.SetDirty(catalog);
        }

        private static void SeedUiStyle(UiStyle s)
        {
            s.ReferenceResolution = new Vector2(1920f, 1080f);
            s.MatchWidthOrHeight = 0.5f;
            s.CircleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(CircleSpritePath);
            if (s.CircleSprite == null) throw new InvalidOperationException("ViewAssetBuilder: built-in sprite not found: " + CircleSpritePath);

            // A.7a toy skin (Art-Spec §4.1): translucent bases with a light rim (the sprite has its own alpha), glossy knobs.
            s.MoveStick = new StickStyle
            {
                Position = new Vector2(320f, 300f), HitSize = 0f, BaseSize = 280f, KnobSize = 130f, Radius = 120f,
                BaseColor = WithAlpha(Hex(0xFFF4DE), 0.6f), KnobColor = WithAlpha(Hex(0xFFF4DE), 0.85f)
            };
            s.AttackStick = new StickStyle
            {
                Position = new Vector2(300f, 290f), HitSize = 380f, BaseSize = 260f, KnobSize = 120f, Radius = 110f,
                BaseColor = WithAlpha(Hex(0xFF7A5C), 0.6f), KnobColor = Hex(0xFF7A5C)
            };
            s.UltStick = new StickStyle
            {
                Position = new Vector2(620f, 190f), HitSize = 260f, BaseSize = 190f, KnobSize = 95f, Radius = 80f,
                BaseColor = WithAlpha(Hex(0xFFC93C), 0.7f), KnobColor = Hex(0xFFC93C)
            };
            s.TrickButtonPosition = new Vector2(290f, 600f);
            s.TrickButtonSize = 150f;
            s.TrickButtonColor = Hex(0x4CBF56);
            s.UltNotReadyBaseColor = WithAlpha(Hex(0xD8D0C0), 0.4f);
            s.UltNotReadyKnobColor = WithAlpha(Hex(0xB8B0A0), 0.75f);
            s.TrickButtonDisabledColor = WithAlpha(Hex(0x8A9A8C), 0.6f);
            s.TrickDotSize = 36f;
            s.TrickDotSpacing = 40f;
            s.TrickDotOffset = 104f;
            s.TrickDotOnColor = Color.white;
            s.TrickDotOffColor = new Color(0.3f, 0.25f, 0.2f, 0.45f);
            // D-129: a press that stays within 0.35 of the radius from the touch point is a tap (auto-aim) at any duration.
            s.Gesture = new StickGestureSettings(0.2f, 0.25f, 0.35f);
            s.AllyColor = AllyColor;
            s.EnemyColor = EnemyColor;
            s.EnemyColorAlt = EnemyAltColor;

            // S2.10 (PROTO): FPS counter in the top-left corner, settings panel rows. A.6: dim warm backdrop under the panel.
            s.FpsMargin = new Vector2(24f, 20f);
            s.FpsBoxSize = new Vector2(220f, 44f);
            s.SettingsPanelColor = new Color(0.10f, 0.12f, 0.07f, 0.72f);
            // D-129: match menu — pause button in the top-right corner (clear of the sticks), panel buttons.
            s.PauseButtonMargin = new Vector2(28f, 24f);
            s.PauseButtonSize = new Vector2(116f, 108f);
            s.PauseIconBarSize = new Vector2(14f, 44f);
            s.PauseIconGap = 14f;
            s.PauseMenuButtonSize = new Vector2(500f, 112f);
            s.PauseMenuWidth = 620f;
            s.SettingsLabelWidth = 520f;
            s.SettingsChoiceSize = new Vector2(150f, 84f);

            // Menu, score bar, results (S2.7, PROTO sizes at 1920×1080 — check on the phone). A.6: palette of Art-Spec §1.3
            // ("wood and leaves"), Nunito Bold / ExtraBold (SIL OFL 1.1).
            s.TextColor = Color.white;
            s.TitleFontSize = 76;
            s.LargeFontSize = 50;
            s.MediumFontSize = 36;
            s.SmallFontSize = 28;
            s.ButtonColor = Hex(0xEBCB8B);
            s.SecondaryButtonColor = Hex(0x6EC6F0);
            s.ButtonSelectedColor = s.SecondaryButtonColor;
            s.PrimaryButtonColor = Hex(0x4CBF56);
            s.Font = LoadFont(BodyFontPath);
            s.HeadingFont = LoadFont(HeadingFontPath);
            s.MenuButtonSize = new Vector2(220f, 84f);
            s.PlayButtonSize = new Vector2(470f, 130f);
            s.MenuRowSpacing = 22f;
            s.MenuItemSpacing = 14f;
            s.VersionMargin = new Vector2(24f, 24f);
            s.ScoreBarTopMargin = 20f;
            s.ScoreBoxSize = new Vector2(184f, 96f);
            s.TimerBoxSize = new Vector2(210f, 96f);
            s.ScoreBarSpacing = 12f;
            s.ScorePanelColor = new Color(0f, 0f, 0f, 0.45f);
            s.CountdownSize = new Vector2(360f, 70f);
            s.ResultsBackdropColor = new Color(0.08f, 0.10f, 0.06f, 0.72f);
            s.MvpCapsuleSize = new Vector2(90f, 160f);
            s.MvpBounceHeight = 30f;
            s.MvpBounceFrequency = 1.5f;
            s.ResultsRowHeight = 48f;
            s.ResultsNameColumnWidth = 360f;
            s.ResultsStatColumnWidth = 200f;
            s.ResultsSpacing = 20f;
            s.ResultsButtonSize = new Vector2(380f, 120f);

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
            s.MatchmakingBackdropColor = new Color(0.10f, 0.12f, 0.07f, 0.94f);

            // A.6 "wood and leaves" skin (Art-Spec §1.3, §4.2): palette, button geometry (canvas units), then the baked sprites.
            s.PanelColor = Hex(0xFFF4DE);
            s.PanelBorderColor = Hex(0x8A5A35);
            s.SliceScale = 1.6f;
            s.ButtonEdgeHeight = 10f;
            s.ButtonOutline = 3.5f;
            s.ButtonPressDepth = 0.7f;
            s.ButtonEdgeShade = 0.72f;
            s.PrimaryButtonEdgeColor = Hex(0x2F8F3A);
            s.SecondaryButtonEdgeColor = Hex(0x3B8FBF);
            s.ButtonGlossAlpha = 0.3f;
            s.ShadowColor = WithAlpha(Hex(0x3B2A1E), 0.35f);
            s.ShadowOffset = new Vector2(0f, -7f);
            s.TextDarkColor = Hex(0x3B2A1E);
            s.TextOutlineColor = Hex(0x3B2A1E);
            s.TextOutlineDistance = new Vector2(2f, -2f);
            s.LightFaceLuma = 0.72f;
            s.PanelPadding = new Vector2(40f, 30f);
            s.LeafSize = new Vector2(130f, 130f);
            s.PlankHeight = 88f;
            s.VictoryTitleColor = Hex(0xFFC93C);
            s.DefeatTitleColor = new Color(0.85f, 0.88f, 0.95f, 1f);
            s.RowStripeColor = WithAlpha(Hex(0x8A5A35), 0.12f);
            s.BackdropTopColor = Hex(0xCDE8B5);
            s.BackdropBottomColor = Hex(0x6E9E58);
            s.BackdropLeafColor = WithAlpha(Hex(0x4F7A45), 0.35f);
            s.PanelSliceScale = 1.0f;
            s.LabelBaselineShift = 0.06f;
            s.MenuLabelWidth = 340f;
            s.DisabledLabelAlpha = 0.6f;
            // A.7a ult flower (8 petals around the ult stick), glow and button icons.
            s.UltPetalCount = 8;
            s.UltPetalSize = new Vector2(54f, 80f);
            s.UltPetalRadius = 118f;
            s.UltPetalOffColor = new Color(1f, 1f, 1f, 0.45f);
            s.UltPetalOnColor = Hex(0xFFAE34);
            s.UltPetalReadyColor = Hex(0xFFE04A);
            s.UltGlowSize = 380f;
            s.UltGlowColor = WithAlpha(Hex(0xFFD84A), 0.7f);
            s.UltIconSize = 62f;
            s.TrickIconSize = 84f;
            s.IconReadyColor = Color.white;
            s.IconNotReadyColor = new Color(1f, 1f, 1f, 0.45f);
            // A.7b score tablets (acorn + number in team colours) and the wooden timer plate.
            s.ScoreIconShare = 0.62f;
            s.ScoreIconMargin = 10f;
            UiSpriteBaker.BakeAll(s);
            EditorUtility.SetDirty(s);
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        private static Font LoadFont(string path)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null) throw new InvalidOperationException("ViewAssetBuilder: font not found: " + path);
            return font;
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

        private static Color Lighten(Color c) => Color.Lerp(c, Color.white, RingLighten);

        private static readonly int BarFrameTextureWidth = Mathf.RoundToInt((BarWidth + BarPadding * 2f) * BarTexturePixelsPerUnit);
        private static readonly int BarFrameTextureHeight = Mathf.RoundToInt((BarHeight + BarPadding * 2f) * BarTexturePixelsPerUnit);

        /// <summary>Unlit transparent material with a texture tinted by <paramref name="color"/>.</summary>
        private static Material SaveTexturedMaterial(string name, Color color, Texture2D texture)
        {
            Material mat = SaveMaterial(name, UnlitShader, color, true);
            mat.SetTexture(BaseMapProperty, texture);
            mat.SetTexture(MainTexProperty, texture);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Team ring (A.7a): dark outline, shaded band, light inner rim, faint disc inside; white, tinted by the team colour.</summary>
        private static Color RingPixel(float x, float y)
        {
            float half = RingTextureSize * 0.5f;
            float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
            if (r > 1f) return Color.clear;
            if (r > 1f - 3f / half) return WithAlpha(UiSpriteBaker.Outline, 0.6f);
            if (r > RingBandInner) return new Color(RingBandShade, RingBandShade, RingBandShade, 1f);
            if (r > RingRimInner) return Color.white;
            return new Color(1f, 1f, 1f, RingDiscAlpha);
        }

        /// <summary>Health bar frame (A.7a): rounded box with the dark outline, dark translucent inside (the missing health).</summary>
        private static Color BarFramePixel(float x, float y)
        {
            float u = x / BarTexturePixelsPerUnit, v = y / BarTexturePixelsPerUnit;
            float hx = BarWidth * 0.5f + BarPadding, hy = BarHeight * 0.5f + BarPadding;
            float d = UiSpriteBaker.RoundBox(u, v, hx, hy, hx, hy, BarFrameRadius);
            if (d > 0f) return Color.clear;
            if (d > -BarFrameOutline) return UiSpriteBaker.Outline;
            return BarFrameInside;
        }

        /// <summary>Charge bead (A.7a): round, dark outline, white face shaded towards the bottom with a gloss spot.</summary>
        private static Color BeadPixel(float x, float y)
        {
            float half = BeadTextureSize * 0.5f;
            float dx = (x - half) / half, dy = (y - half) / half;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > 1f) return Color.clear;
            if (r > 0.8f) return UiSpriteBaker.Outline;
            float shade = Mathf.Lerp(0.78f, 1f, (dy + 1f) * 0.5f);
            float gx = dx + 0.3f, gy = dy - 0.35f;
            if (gx * gx + gy * gy < 0.06f) shade = 1f;
            Color c = new Color(shade, shade, shade, 1f);
            return gx * gx + gy * gy < 0.06f ? Color.Lerp(c, Color.white, 0.8f) : c;
        }

        /// <summary>Texture drawn by <paramref name="pixel"/> with 4×4 samples per pixel (straight alpha), saved as PNG, imported clamped.</summary>
        private static Texture2D BuildHudTexture(string path, int w, int h, Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                const int samples = 4;
                var pixels = new Color[w * h];
                for (int py = 0; py < h; py++)
                for (int px = 0; px < w; px++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        Color c = pixel(px + (sx + 0.5f) / samples, py + (sy + 0.5f) / samples);
                        r += c.r * c.a;
                        g += c.g * c.a;
                        b += c.b * c.a;
                        a += c.a;
                    }
                    pixels[py * w + px] = a > 0f ? new Color(r / a, g / a, b / a, a / (samples * samples)) : new Color(1f, 1f, 1f, 0f);
                }
                tex.SetPixels(pixels);
                tex.Apply();
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

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

        /// <summary>Unity's capsule copied into an asset with vertex colours (keeps the GUID on re-runs).</summary>
        private static Mesh BuildFallbackCapsuleMesh()
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                Mesh source = temp.GetComponent<MeshFilter>().sharedMesh;
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FallbackCapsuleMeshPath);
                bool created = mesh == null;
                if (created) mesh = new Mesh();
                mesh.Clear();
                mesh.name = "FallbackCapsule";
                mesh.vertices = source.vertices;
                mesh.normals = source.normals;
                mesh.uv = source.uv;
                var colors = new Color32[source.vertexCount];
                Color32 vertexColor = FallbackCapsuleColor.linear;
                vertexColor.a = FallbackCapsuleGloss;
                for (int i = 0; i < colors.Length; i++) colors[i] = vertexColor;
                mesh.colors32 = colors;
                mesh.triangles = source.triangles;
                mesh.RecalculateBounds();
                if (created) AssetDatabase.CreateAsset(mesh, FallbackCapsuleMeshPath);
                else EditorUtility.SetDirty(mesh);
                return mesh;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        private static FighterView BuildFighterViewPrefab(Material body, Material ring, Material nose, Material shadow, Mesh bodyMesh)
        {
            var root = new GameObject("FighterView");
            try
            {
                (Renderer ringRenderer, Renderer shadowRenderer) = AddRingAndShadow(root.transform, ring, shadow);

                GameObject capsule = CreatePart(PrimitiveType.Capsule, "Body", root.transform, body);
                capsule.GetComponent<MeshFilter>().sharedMesh = bodyMesh;
                // Unity capsule: height 2, diameter 1.
                capsule.transform.localScale = new Vector3(BodyDiameter, BodyHeight * 0.5f, BodyDiameter);
                capsule.transform.localPosition = new Vector3(0f, BodyHeight * 0.5f, 0f);

                GameObject noseGo = CreatePart(PrimitiveType.Cube, "Nose", root.transform, nose);
                noseGo.transform.localScale = NoseScale;
                noseGo.transform.localPosition = new Vector3(0f, BodyHeight * NoseHeightFraction, NoseForward);

                var view = root.AddComponent<FighterView>();
                var so = new SerializedObject(view);
                SetRef(so, "_body", capsule.transform);
                SetRef(so, "_ring", ringRenderer);
                SetRef(so, "_shadow", shadowRenderer);
                so.ApplyModifiedPropertiesWithoutUndo();

                return SavePrefab(root, FighterViewPrefabPath).GetComponent<FighterView>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Blob shadow (D-027) and team ring of a fighter view, shared by the fallback capsule and the procedural models.</summary>
        internal static (Renderer ring, Renderer shadow) AddRingAndShadow(Transform root, Material ring, Material shadow)
        {
            // Blob shadow: Unity quad faces −Z, turned to lie on the ground.
            GameObject shadowGo = CreatePart(PrimitiveType.Quad, "Shadow", root, shadow);
            shadowGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadowGo.transform.localScale = new Vector3(BlobShadowDiameter, BlobShadowDiameter, 1f);
            shadowGo.transform.localPosition = new Vector3(0f, BlobShadowHeight, 0f);

            // A.7a: a flat textured ring (band + light rim), like the shadow a quad lying on the ground.
            GameObject ringGo = CreatePart(PrimitiveType.Quad, "Ring", root, ring);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringGo.transform.localScale = new Vector3(RingDiameter, RingDiameter, 1f);
            ringGo.transform.localPosition = new Vector3(0f, RingHeight, 0f);
            return (ringGo.GetComponent<Renderer>(), shadowGo.GetComponent<Renderer>());
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

        /// <summary>Meadow around the arena (A.3a): an empty MeshFilter (MapView assigns MapMeshBuilder.BuildMeadow) and a renderer.</summary>
        private static GameObject BuildMeadowPrefab(Material meadow)
        {
            var root = new GameObject("Meadow");
            try
            {
                root.AddComponent<MeshFilter>();
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = meadow;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return SavePrefab(root, FloorPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Arena/ToonEnv material (reset to the shader defaults), optional world-UV map and its mean luminance.</summary>
        private static Material SaveEnvMaterial(string name, Color tint, Texture2D map, float mapMeanLuminance, float contrast)
        {
            Material mat = SaveToonMaterial(name, ToonEnvShader, tint, 0f);
            mat.SetTexture(BaseMapProperty, map);
            mat.SetFloat(MapMeanLuminanceProperty, map != null ? Mathf.Max(0.05f, mapMeanLuminance) : 1f);
            mat.SetFloat(WorldTilingProperty, FloorWorldTiling);
            mat.SetFloat(MacroTilingProperty, FloorMacroTiling);
            mat.SetFloat(MacroStrengthProperty, map != null ? FloorMacroStrength : 0f);
            mat.SetFloat(MapContrastProperty, contrast);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// <c>Arena/ToonEnv</c> material of a textured prop (A.3b): top map in world XZ (<paramref name="topTiling"/> units per tile)
        /// and side map along the faces, both divided by their mean luminance (detail only, the vertex colours keep the palette).
        /// </summary>
        private static Material SavePropMaterial(string name, Color tint, Texture2D top, float topMean, float topTiling, Texture2D side,
            float sideMean, float sideTiling, float sideContrast)
        {
            Material mat = SaveToonMaterial(name, ToonEnvShader, tint, 0f);
            mat.SetTexture(BaseMapProperty, top);
            mat.SetFloat(MapMeanLuminanceProperty, Mathf.Max(0.05f, topMean));
            mat.SetFloat(BaseNormalizeProperty, 1f);
            mat.SetFloat(WorldTilingProperty, topTiling);
            mat.SetFloat(MacroStrengthProperty, 0f);
            mat.SetFloat(MapContrastProperty, 1f);
            mat.SetTexture(SideMapProperty, side);
            mat.SetFloat(SideMeanProperty, Mathf.Max(0.05f, sideMean));
            mat.SetFloat(SideNormalizeProperty, 1f);
            mat.SetFloat(SideTilingProperty, sideTiling);
            mat.SetFloat(SideContrastProperty, sideContrast);
            mat.SetFloat(ReceiveMapShadowProperty, 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary><c>Arena/Bush</c>: opaque, or the see-through variant (alpha blended, no depth write, Transparent queue, D-024).</summary>
        private static Material SaveBushMaterial(string name, Color color, Texture2D leaves, float leavesMean, bool seeThrough)
        {
            Material mat = SaveToonMaterial(name, BushShader, color, 0f);
            mat.SetTexture(LeafMapProperty, leaves);
            mat.SetFloat(LeafMeanProperty, Mathf.Max(0.05f, leavesMean));
            mat.SetFloat(LeafTilingProperty, LeafTiling);
            mat.SetFloat(LeafContrastProperty, LeafContrast);
            if (seeThrough)
            {
                mat.SetFloat(SrcBlendProperty, SeeThroughSrcBlend);
                mat.SetFloat(DstBlendProperty, SeeThroughDstBlend);
                mat.SetFloat(ZWriteProperty, 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Bush variants (A.3b) as mesh assets; MapView picks one per cell.</summary>
        private static Mesh[] SaveEnvMeshes()
        {
            // A.3b blob bushes (Bush_0..2) are replaced by tall-grass clumps per neighbour mask (A.3c).
            for (int i = 0; i < 3; i++) AssetDatabase.DeleteAsset(EnvMeshesFolder + "/Bush_" + i + ".asset");
            var meshes = new Mesh[EnvMeshBuilder.BushMaskCount * EnvMeshBuilder.BushVariantCount];
            for (int mask = 0; mask < EnvMeshBuilder.BushMaskCount; mask++)
                for (int v = 0; v < EnvMeshBuilder.BushVariantCount; v++)
                    meshes[mask * EnvMeshBuilder.BushVariantCount + v] = SaveMesh(EnvMeshBuilder.BuildBush(mask, v),
                        EnvMeshesFolder + "/Bush_m" + mask + "_v" + v + ".asset");
            return meshes;
        }

        /// <summary>Saves a generated mesh as an asset (overwrites in place: references and the GUID stay).</summary>
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            ProjectConfigurator.EnsureFolder(EnvMeshesFolder);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
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

        /// <summary>A prop of one mesh (pivot at the bottom centre): root + "Mesh" child with a filter and a renderer, no shadows.</summary>
        private static GameObject BuildMeshPrefab(string name, string path, Mesh mesh, Material material)
        {
            var root = new GameObject(name);
            try
            {
                var child = new GameObject("Mesh");
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return SavePrefab(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Quad of 1×1 cell on the XZ plane facing up, centred (floor, fog strips).</summary>
        private static GameObject BuildQuadPrefab(string name, string path, Material material)
        {
            var root = new GameObject(name);
            try
            {
                GameObject quad = CreatePart(PrimitiveType.Quad, "Quad", root.transform, material);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
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

        private static HealthBarView BuildHealthBarPrefab(Material fill, Material background, Material charge, Material socket,
            Material gloss)
        {
            var root = new GameObject("HealthBar");
            try
            {
                // Quads face −Z; the bar takes the camera rotation, so "towards the camera" is −Z.
                GameObject back = CreatePart(PrimitiveType.Quad, "Background", root.transform, background);
                back.transform.localScale = new Vector3(BarWidth + BarPadding * 2f, BarHeight + BarPadding * 2f, 1f);

                var pivot = new GameObject("FillPivot");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(-BarWidth * 0.5f, 0f, -LayerOffset);
                GameObject fillGo = CreatePart(PrimitiveType.Quad, "Fill", pivot.transform, fill);
                fillGo.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);
                fillGo.transform.localPosition = new Vector3(BarWidth * 0.5f, 0f, 0f);
                GameObject glossGo = CreatePart(PrimitiveType.Quad, "Gloss", pivot.transform, gloss);
                glossGo.transform.localScale = new Vector3(BarWidth, BarHeight * BarGlossShare, 1f);
                glossGo.transform.localPosition = new Vector3(BarWidth * 0.5f, BarHeight * (0.5f - BarGlossShare * 0.5f), -LayerOffset);

                // Beads under the frame, centred; the own fighter's are OwnBeadScale bigger (spacing leaves room for that).
                var segments = new Renderer[ChargeSegments];
                var sockets = new Renderer[ChargeSegments];
                float y = -(BarHeight * 0.5f + BarPadding + ChargeGap + ChargeBead * 0.5f);
                for (int i = 0; i < ChargeSegments; i++)
                {
                    float x = (i - (ChargeSegments - 1) * 0.5f) * ChargeBeadSpacing;
                    GameObject sock = CreatePart(PrimitiveType.Quad, "Socket" + i, root.transform, socket);
                    sock.transform.localScale = new Vector3(ChargeBead, ChargeBead, 1f);
                    sock.transform.localPosition = new Vector3(x, y, 0f);
                    sockets[i] = sock.GetComponent<Renderer>();
                    GameObject seg = CreatePart(PrimitiveType.Quad, "Charge" + i, root.transform, charge);
                    seg.transform.localScale = new Vector3(ChargeBead, ChargeBead, 1f);
                    seg.transform.localPosition = new Vector3(x, y, -LayerOffset);
                    segments[i] = seg.GetComponent<Renderer>();
                }

                var view = root.AddComponent<HealthBarView>();
                var so = new SerializedObject(view);
                SetRef(so, "_fillPivot", pivot.transform);
                SetRef(so, "_fill", fillGo.GetComponent<Renderer>());
                SetRendererArray(so, "_chargeSegments", segments);
                SetRendererArray(so, "_chargeSockets", sockets);
                SetFloat(so, "_height", BarAboveFeet);
                SetFloat(so, "_aboveTop", BarAboveTop);
                SetFloat(so, "_beadSize", ChargeBead);
                SetFloat(so, "_ownBeadScale", OwnBeadScale);
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

        /// <summary>
        /// Material on an <c>Arena/*</c> toon shader: reset to the shader's defaults each run (no stale URP properties), then the
        /// base colour (alpha = opacity of the ghost) and the flash amount. Render queue from the shader.
        /// </summary>
        private static Material SaveToonMaterial(string name, string shaderName, Color color, float flash)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("ViewAssetBuilder: shader not found: " + shaderName);
            string path = MaterialsFolder + "/" + name + ".mat";
            var fresh = new Material(shader) { name = name };
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = fresh;
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                EditorUtility.CopySerialized(fresh, mat);
                mat.name = name;
                Object.DestroyImmediate(fresh);
            }
            mat.SetColor(BaseColorProperty, color);
            if (mat.HasProperty(FlashProperty)) mat.SetFloat(FlashProperty, flash);
            mat.renderQueue = -1;
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

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) throw new InvalidOperationException("ViewAssetBuilder: field not found: " + field);
            p.floatValue = value;
        }

        private static void SetRendererArray(SerializedObject so, string field, Renderer[] values)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) throw new InvalidOperationException("ViewAssetBuilder: field not found: " + field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
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
