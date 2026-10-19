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

        [Tooltip("Fort corner / pier with a lantern post on top (wall 'L', D-142).")]
        public Mesh FortLantern;

        [Tooltip("Small snow lump at the foot of unbreakable decor walls (D-142, view only).")]
        public Mesh WallLump;

        [Tooltip("Chance of a lump on each open side of a decor wall and per unit along the frame wall.")]
        [Range(0f, 1f)] public float WallLumpChance;

        [Header("Frame wall around the arena (D-142)")]
        [Tooltip("Wall segment along +X (pivot at the bottom centre) of length FrameWallSegment; empty = no frame wall.")]
        public Mesh FrameWall;
        public Mesh FramePost;

        [Tooltip("Garland from one post (x = 0) to the next (x = FramePostSpacing) along +X.")]
        public Mesh FrameGarland;

        [Tooltip("Distance of the wall centre line outside the arena edge, world units.")]
        [Min(0f)] public float FrameWallOffset;
        [Min(0.1f)] public float FrameWallSegment = 1f;
        [Min(0.1f)] public float FramePostSpacing = 2f;

        [Tooltip("Height scale of the near (camera) side of the frame: low enough not to hide the first row.")]
        [Range(0.1f, 1f)] public float FrameNearHeight = 1f;

        [Tooltip("Distance of the snow lumps along the frame wall from its centre line towards the field, world units.")]
        [Min(0f)] public float FrameLumpDistance;

        [Tooltip("Cabins, woodpiles and snow mounds behind the left, right and far walls.")]
        public Mesh[] FrameExtras;

        [Tooltip("Distance of the frame extras outside the arena edge and between them along a side, world units.")]
        [Min(0f)] public float FrameExtraOffset;
        [Min(0f)] public float FrameExtraSpacing;

        [Header("Footprint trails on the floor (D-142)")]
        [Tooltip("Trails of footprints from every spawn towards the centre (view only).")]
        public bool Trails;
        public Color TrailColor = Color.white;

        [Tooltip("Distance between prints along a trail, half the distance between the left and right prints, world units.")]
        [Min(0.05f)] public float TrailStep = 0.45f;
        [Min(0f)] public float TrailSideOffset = 0.12f;

        [Tooltip("Print half size: across, along the trail.")]
        public Vector2 TrailPrintSize = new Vector2(0.07f, 0.11f);

        [Tooltip("Trails stop this many cells (Chebyshev) from the map centre.")]
        [Min(0)] public int TrailStopCells = 4;

        [Tooltip("Random sideways shift of a whole trail (± world units) so trails that merge do not print over each other.")]
        [Min(0f)] public float TrailSpread = 0.18f;

        [Header("Frame trees around the arena (D-140; the snow theme uses the frame wall instead, D-142)")]
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
