using System;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Floor details as geometry (A.3e, brief A-EnvTarget §1.2 #4/#10): stepping-stone paths, rare pebbles, sprouts and flowers.
    /// Planned by <see cref="FloorDecorPlanner"/> at map load, drawn as one mesh by <see cref="FloorDecorMeshBuilder"/> with the floor
    /// material (one draw call after static batching, gets the ground shadows and contact occlusion). Colours sRGB.
    /// Seeded by <c>ViewAssetBuilder</c>, stored in <see cref="ViewCatalog.DecorStyle"/>.
    /// </summary>
    [Serializable]
    public struct EnvDecorStyle
    {
        [Header("Paths of stepping stones")]
        [Tooltip("Paths as map fractions: x, y = from, z, w = to (0..1 of width/height). Each path is mirrored through the map centre.")]
        public Vector4[] Paths;

        [Tooltip("Distance between stone steps along a path, world units (0 = no paths).")]
        [Min(0f)] public float PathStep;

        [Tooltip("Lateral offset of the two stones of a double step from the path line, world units.")]
        [Min(0f)] public float PathHalfWidth;

        [Tooltip("Random lateral and along-path offset of a stone, world units.")]
        [Min(0f)] public float PathJitter;

        [Tooltip("Chance that a step has two stones side by side.")]
        [Range(0f, 1f)] public float PathPairChance;

        [Tooltip("Stone radius range, world units.")]
        public Vector2 StoneRadius;

        [Tooltip("Stone minor/major axis ratio range.")]
        public Vector2 StoneAspect;

        [Tooltip("Height of a stone's top above the sand, world units.")]
        [Min(0.001f)] public float StoneHeight;

        public Color StoneTop;

        [Tooltip("Stone colour at the sand line (dark edge = contact shade baked into the vertices).")]
        public Color StoneEdge;

        [Tooltip("Keep stones this far from blocked cells (walls, water, bushes), world units.")]
        [Min(0f)] public float ClearMargin;

        [Header("Scattered details (chance per free floor cell)")]
        [Range(0f, 1f)] public float PebbleChance;
        [Range(0f, 1f)] public float SproutChance;
        [Range(0f, 1f)] public float FlowerChance;

        [Tooltip("Pebble radius range, world units.")]
        public Vector2 PebbleRadius;

        public Color PebbleTop;
        public Color PebbleEdge;

        [Tooltip("Sprout leaf length, world units.")]
        [Min(0.01f)] public float SproutSize;

        public Color SproutBase;
        public Color SproutTip;

        [Tooltip("Flower radius, world units.")]
        [Min(0.01f)] public float FlowerSize;

        [Tooltip("Petal colours (a flower takes one).")]
        public Color[] FlowerPetals;

        public Color FlowerCentre;

        [Tooltip("No scattered detail closer than this to a path stone, world units.")]
        [Min(0f)] public float StoneClearance;

        [Tooltip("Triangle budget of all floor details of a map (brief §3.3: ≤ 5000).")]
        [Min(0)] public int MaxTriangles;
    }
}
