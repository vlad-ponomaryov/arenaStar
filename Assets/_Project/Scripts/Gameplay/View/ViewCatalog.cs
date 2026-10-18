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

        [Header("Projectiles")]
        [Tooltip("Views created at match start; the pool grows only if more projectiles fly at once.")]
        [Min(0)] public int ProjectilePoolPrewarm;

        [Tooltip("Flight height above the ground, world units (view only).")]
        [Min(0f)] public float ProjectileHeight;

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
    }
}
