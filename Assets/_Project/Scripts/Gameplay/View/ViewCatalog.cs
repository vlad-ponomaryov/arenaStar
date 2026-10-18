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

        [Header("Materials")]
        [Tooltip("Ring under the local player's team (blue, D-029).")]
        public Material TeamAlly;

        [Tooltip("Ring under the enemy team (red, D-029).")]
        public Material TeamEnemy;

        public Material FighterBody;
        public Material Floor;
        public Material AimLine;
    }
}
