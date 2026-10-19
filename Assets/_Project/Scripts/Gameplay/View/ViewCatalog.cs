using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// References to view prefabs and shared materials (D-014). Built by <c>Arena.Editor.Art.ViewAssetBuilder</c>;
    /// replacing the art = editing the prefabs/materials, not the code.
    /// </summary>
    [CreateAssetMenu(fileName = "ViewCatalog", menuName = "Arena/View Catalog")]
    public sealed class ViewCatalog : ScriptableObject
    {
        [Header("Prefabs")]
        public FighterView FighterViewPrefab;
        public AimIndicatorView AimIndicatorPrefab;

        [Tooltip("Meadow around the arena (A.3a): MeshFilter + MeshRenderer with the meadow material; MapView gives it the frame mesh (MapMeshBuilder.BuildMeadow).")]
        public GameObject FloorPrefab;

        [Tooltip("Unit-diameter projectile view (sphere), scaled to the projectile's hit diameter.")]
        public GameObject ProjectilePrefab;

        public HealthBarView HealthBarPrefab;

        [Tooltip("Ground marker of a pending area attack (zone disc + growing fill), diameter 1.")]
        public TelegraphView TelegraphPrefab;

        [Tooltip("Fighter view prefab by fighter id (D-113); a fighter without an entry (or with an empty prefab) uses FighterViewPrefab.")]
        public FighterVisual[] FighterVisuals;

        [Header("Areas and special projectiles (S2.11)")]
        [Tooltip("Rolling log: cylinder with its axis along local X, length 1 and diameter 1; scaled at runtime.")]
        public GameObject LogPrefab;

        [Tooltip("Thickness (diameter) of a rolling log, world units (view only).")]
        [Min(0.01f)] public float LogThickness;

        [Tooltip("Lobbed bomb in flight, pivot at the centre, real size.")]
        public GameObject LobBombPrefab;

        [Tooltip("Peak height of a lob's arc above the straight line, world units (view only).")]
        [Min(0f)] public float LobArcHeight;

        [Tooltip("Placed mine, pivot at the bottom centre, real size.")]
        public GameObject MinePrefab;

        [Tooltip("Dome of a field area: sphere of diameter 1 (half under the ground); scaled to the area diameter. Its renderer gets the team's zone material.")]
        public GameObject DomePrefab;

        [Tooltip("Breakable wall: a wooden crate of 1 cell (A.3b), pivot at the bottom centre.")]
        public GameObject BreakableWallPrefab;

        [Tooltip("Bush of 1 cell, pivot at the bottom centre: one MeshFilter + renderer (gets Bush / BushSeeThrough, D-024).")]
        public GameObject BushPrefab;

        [Tooltip("Tall-grass bush meshes (A.3c): index = neighbour mask (W 1, E 2, S 4, N 8) × BushVariantsPerMask + variant. Sides " +
                 "towards a bush neighbour run to the cell edge at a shared height, so neighbouring cells merge into one mass.")]
        public Mesh[] BushVariants;

        [Tooltip("Variants per neighbour mask in BushVariants (MapView picks one per cell, deterministic).")]
        [Min(1)] public int BushVariantsPerMask = 1;

        [Header("Obstacle looks (A.3c, view only)")]
        [Tooltip("Prop meshes a small wall group may be drawn as instead of stone blocks (barrel, stump, boulder…); pivot at the " +
                 "bottom centre of the cell, drawn with ObstacleProp. One type per connected wall group, chosen by a symmetric hash.")]
        public Mesh[] ObstacleProps;

        [Tooltip("Arena/ToonEnv material of the obstacle props (vertex colours).")]
        public Material ObstacleProp;

        [Tooltip("Wall groups larger than this (cells) always stay stone blocks; smaller ones may become props.")]
        [Min(0)] public int ObstaclePropMaxCells;

        [Tooltip("Every N-th small wall group (counting mirrored twins as one) stays stone blocks; 0 = none.")]
        [Min(0)] public int ObstacleStoneEvery;

        [Header("Acorns mode (Arena.Modes.AcornsModeView)")]
        [Tooltip("Normal acorn view, pivot at the centre.")]
        public GameObject AcornPrefab;

        [Tooltip("Golden acorn view (worth 3).")]
        public GameObject AcornGoldenPrefab;

        [Tooltip("Hollow in the map centre, 1 cell, pivot at the bottom centre.")]
        public GameObject HollowPrefab;

        [Tooltip("Acorn views created at match start per kind.")]
        [Min(0)] public int AcornPoolPrewarm;

        [Tooltip("Height of an acorn above the ground, world units (view only).")]
        [Min(0f)] public float AcornHeight;

        [Tooltip("Spin of lying acorns, degrees per second (view only).")]
        public float AcornSpinSpeed;

        [Header("Last Beast mode (S3.14: berry boxes in MapView, Arena.Modes.LastBeastModeView)")]
        [Tooltip("Berry box cube of 1 cell, pivot at the bottom centre (a map tile, removed when broken).")]
        public GameObject BerryBoxPrefab;

        [Tooltip("Berry view, pivot at the centre.")]
        public GameObject BerryPrefab;

        [Tooltip("Fog strip: quad of 1×1 cell on the XZ plane, centred, translucent; scaled at runtime.")]
        public GameObject FogPrefab;

        [Tooltip("Berry views created at match start.")]
        [Min(0)] public int BerryPoolPrewarm;

        [Tooltip("Height of a berry above the ground, world units (view only).")]
        [Min(0f)] public float BerryHeight;

        [Tooltip("Bobbing of lying berries: amplitude (world units) and speed (radians per second).")]
        [Min(0f)] public float BerryBobHeight;
        public float BerryBobSpeed;

        [Tooltip("Height of the fog strips above the ground, world units.")]
        [Min(0f)] public float FogHeight;

        [Tooltip("The shown fog edge follows a new step with this sharpness (1/s); 0 — at once.")]
        [Min(0f)] public float FogFollowSharpness;

        [Header("Projectiles")]
        [Tooltip("Views created at match start; the pool grows only if more projectiles fly at once.")]
        [Min(0)] public int ProjectilePoolPrewarm;

        [Tooltip("Flight height above the ground, world units (view only).")]
        [Min(0f)] public float ProjectileHeight;

        [Header("Map")]
        [Tooltip("Height of the combined wall mesh, world units (match the breakable wall prefab).")]
        [Min(0f)] public float WallHeight;

        [Tooltip("Height of the water sheet relative to the floor, world units; below 0 = a sunken pond with banks (A.3b).")]
        public float WaterHeight;

        [Tooltip("Banks of a sunken pond reach this far below the water sheet, world units (no gap at the waterline).")]
        [Min(0f)] public float BankOverlap;

        [Tooltip("While the local fighter is in a bush, bushes within this radius (cells) turn see-through.")]
        [Min(0f)] public float BushSeeThroughRadius;

        [Tooltip("Wall chamfer and colours, floor occlusion near walls, meadow frame (A.3a, MapMeshBuilder).")]
        public MapMeshStyle MapStyle;

        [Header("Materials")]
        [Tooltip("Ring under the local player's team (blue, D-029).")]
        public Material TeamAlly;

        [Tooltip("Ring under the enemy team (red, D-029).")]
        public Material TeamEnemy;

        [Tooltip("Enemy ring / health bar in the colour-blind palette (orange, D-029).")]
        public Material TeamEnemyAlt;
        public Material BarEnemyAlt;

        [Tooltip("Body of a fighter for a moment after a hit (GDD §5.3).")]
        public Material FighterFlash;

        [Header("Area telegraph materials (translucent): zone and growing fill, by team; enemy also in the colour-blind palette")]
        public Material TelegraphAllyZone;
        public Material TelegraphAllyFill;
        public Material TelegraphEnemyZone;
        public Material TelegraphEnemyFill;
        public Material TelegraphEnemyAltZone;
        public Material TelegraphEnemyAltFill;

        public Material FighterBody;
        public Material Floor;
        public Material AimLine;
        public Material Projectile;

        [Tooltip("Health bar fill of the local player's team / of enemies.")]
        public Material BarAlly;
        public Material BarEnemy;
        public Material BarBackground;
        public Material BarCharge;

        [Header("Map and hiding materials")]
        public Material Wall;
        public Material WallBreakable;
        public Material Bush;
        public Material BushSeeThrough;
        public Material Water;

        [Tooltip("Meadow frame around the arena (darker grass, Arena/ToonEnv, A.3a).")]
        public Material Meadow;

        [Tooltip("Body of an own-team fighter hidden in a bush (translucent, D-014, D-024).")]
        public Material FighterHidden;

        [Header("Acorns mode materials")]
        public Material Acorn;
        public Material AcornGolden;
        public Material Hollow;

        [Header("Last Beast mode materials")]
        public Material BerryBox;
        public Material Berry;
        public Material Fog;

        [Header("Light (A.1a, MatchLightRig)")]
        [Tooltip("Pitch of the directional light, degrees.")]
        [Range(0f, 90f)] public float LightPitch;

        [Tooltip("Light yaw = camera yaw + this, degrees (lit from behind the camera, slightly from the side).")]
        public float LightYawFromCamera;

        /// <summary>The view prefab of the fighter with <paramref name="fighterId"/>, else <see cref="FighterViewPrefab"/>.
        /// No allocations.</summary>
        public FighterView FighterPrefab(string fighterId)
        {
            if (FighterVisuals != null && !string.IsNullOrEmpty(fighterId))
                for (int i = 0; i < FighterVisuals.Length; i++)
                    if (FighterVisuals[i].Prefab != null && string.Equals(FighterVisuals[i].FighterId, fighterId, System.StringComparison.Ordinal))
                        return FighterVisuals[i].Prefab;
            return FighterViewPrefab;
        }

        /// <summary>Showcase prefab of the fighter for menus (D-122): its hero model, else the match prefab.</summary>
        public FighterView FighterHeroPrefab(string fighterId)
        {
            if (FighterVisuals != null && !string.IsNullOrEmpty(fighterId))
                for (int i = 0; i < FighterVisuals.Length; i++)
                    if (FighterVisuals[i].HeroPrefab != null && string.Equals(FighterVisuals[i].FighterId, fighterId, System.StringComparison.Ordinal))
                        return FighterVisuals[i].HeroPrefab;
            return FighterPrefab(fighterId);
        }
    }
}
