using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// Look of one map (D-140, Map-SnowValley-LB §2.2 Р2): materials of the floor, frame, bushes, water and fog, a few colour overrides
    /// of the runtime map meshes and the prop meshes of the map's decor layer (<see cref="MapConfig.Decor"/>, legend in
    /// <see cref="MapDecor"/>). View only: the simulation and the network never read it. A field left empty keeps the shared look of the
    /// view catalog. Built by <c>Arena.Editor.Art.MapProps.SnowThemeBuilder</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "MapTheme", menuName = "Arena/Map Theme")]
    public sealed class MapThemeConfig : ScriptableObject
    {
        public string Id;

        [Header("Materials (empty = the view catalog's)")]
        public Material Floor;
        public Material Meadow;
        public Material Bush;
        public Material BushSeeThrough;
        public Material Water;
        public Material Fog;

        [Tooltip("Arena/ToonEnv material (vertex colours) of the decor props, the frame trees and the snow piles.")]
        public Material Props;

        [Tooltip("Bush meshes by neighbour mask, like ViewCatalog.BushVariants (snow-capped conifers); empty = the catalog's.")]
        public Mesh[] BushVariants;

        [Header("Map mesh colours (MapMeshBuilder)")]
        [Tooltip("Camera background behind the frame.")]
        public Color Background = Color.black;
        public Color FloorOcclusionColor = Color.white;
        public Color MeadowEdge = Color.white;
        public Color MeadowInner = Color.white;

        [Tooltip("Share of the floor covered by the patch map (the snow material's patches are drifts of a cooler white).")]
        [Range(0f, 1f)] public float FloorPatchCoverage;

        [Tooltip("Stepping stones, pebbles, sprouts and flowers of the meadow theme (FloorDecorPlanner); off on snow.")]
        public bool FloorDecor;

        [Header("Decor props (pivot at the bottom centre of the cell)")]
        [Tooltip("Spruce on the centre of a 'T' plus (one object for the five cells).")]
        public Mesh Tree;

        [Tooltip("Gift box variants (one per cell by a hash, D-136: no team colours).")]
        public Mesh[] Gifts;
        public Mesh GiftBig;

        [Tooltip("Snowman variants (breakable wall 'm').")]
        public Mesh[] Snowmen;

        [Tooltip("Snow bricks of a fort (breakable wall 'f').")]
        public Mesh FortBrick;

        [Tooltip("Fort corner / pier: snow bricks on a wooden plinth (wall 'f').")]
        public Mesh FortCorner;
        public Mesh Drift;
        public Mesh Sled;

        [Tooltip("Flat snow pile left by a broken snowman or snow brick (no collision).")]
        public Mesh SnowPile;

        [Header("Frame around the arena")]
        [Tooltip("Spruce meshes planted around the left, right and far sides of the arena (not the near side: it would hide the field).")]
        public Mesh[] FrameTrees;

        [Tooltip("Distance between frame trees along a side, world units; 0 = no frame trees.")]
        [Min(0f)] public float FrameTreeSpacing;

        [Tooltip("Distance of the first ring of frame trees from the arena edge, world units.")]
        [Min(0f)] public float FrameTreeOffset;

        [Tooltip("Rings of frame trees and the distance between rings, world units.")]
        [Min(0)] public int FrameTreeRings;
        [Min(0f)] public float FrameTreeRingStep;

        [Tooltip("Random scale spread of frame trees (± fraction).")]
        [Range(0f, 0.5f)] public float FrameTreeScaleJitter;

        [Tooltip("Random yaw spread of snowmen around facing the camera, degrees.")]
        [Range(0f, 90f)] public float SnowmanYawJitter;
    }
}
