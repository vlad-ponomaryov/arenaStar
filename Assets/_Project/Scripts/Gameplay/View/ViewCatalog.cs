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

        [Tooltip("Ground quad of 1×1 cell on the XZ plane, centred; scaled to the arena size at runtime.")]
        public GameObject FloorPrefab;

        [Tooltip("Unit-diameter projectile view (sphere), scaled to the projectile's hit diameter.")]
        public GameObject ProjectilePrefab;

        public HealthBarView HealthBarPrefab;

        [Tooltip("Breakable wall cube of 1 cell, pivot at the bottom centre.")]
        public GameObject BreakableWallPrefab;

        [Tooltip("Low bush cube of 1 cell, pivot at the bottom centre (its renderer gets Bush / BushSeeThrough).")]
        public GameObject BushPrefab;

        [Header("Projectiles")]
        [Tooltip("Views created at match start; the pool grows only if more projectiles fly at once.")]
        [Min(0)] public int ProjectilePoolPrewarm;

        [Tooltip("Flight height above the ground, world units (view only).")]
        [Min(0f)] public float ProjectileHeight;

        [Header("Map")]
        [Tooltip("Height of the combined wall mesh, world units (match the breakable wall prefab).")]
        [Min(0f)] public float WallHeight;

        [Tooltip("Height of the water sheet above the floor, world units.")]
        [Min(0f)] public float WaterHeight;

        [Tooltip("While the local fighter is in a bush, bushes within this radius (cells) turn see-through.")]
        [Min(0f)] public float BushSeeThroughRadius;

        [Header("Materials")]
        [Tooltip("Ring under the local player's team (blue, D-029).")]
        public Material TeamAlly;

        [Tooltip("Ring under the enemy team (red, D-029).")]
        public Material TeamEnemy;

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

        [Tooltip("Body of an own-team fighter hidden in a bush (translucent, D-014, D-024).")]
        public Material FighterHidden;
    }
}
