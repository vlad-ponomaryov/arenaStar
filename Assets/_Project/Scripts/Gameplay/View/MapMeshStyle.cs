using System;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Look of the runtime map meshes (Art-Spec §2.1, A.3a): wall chamfer and vertex colours, floor occlusion near walls, the
    /// meadow frame around the arena. Colours are authored in sRGB; <see cref="MapMeshBuilder"/> writes them linear into the
    /// vertices (the project renders in linear space). Seeded by <c>ViewAssetBuilder</c>, stored in <see cref="ViewCatalog"/>.
    /// </summary>
    [Serializable]
    public struct MapMeshStyle
    {
        [Tooltip("Chamfer of the wall's upper edges (world units): the top is inset by it and a 45° strip goes down to the side.")]
        [Min(0f)] public float WallBevel;

        [Tooltip("Top of a wall (light stone).")]
        public Color WallTop;

        [Tooltip("Chamfer colour = top blended towards white by this (a light rim: the block reads rounded, like a toy).")]
        [Range(0f, 1f)] public float WallBevelHighlight;

        [Tooltip("Sides of a wall (warm wood).")]
        public Color WallSide;

        [Tooltip("Side colour multiplier at the wall's foot (contact occlusion), 1 = none.")]
        [Range(0f, 1f)] public float WallFootShade;

        [Tooltip("Random brightness spread per wall cell (± fraction), so long walls read as separate blocks.")]
        [Range(0f, 0.2f)] public float WallCellVariation;

        [Tooltip("Floor vertices per cell side (≥ 1): finer = tighter occlusion near walls.")]
        [Min(1)] public int FloorSubdivisions;

        [Tooltip("Floor darkening right at a wall (0 = none, 1 = black).")]
        [Range(0f, 1f)] public float FloorWallOcclusion;

        [Tooltip("Distance from a wall over which the floor occlusion fades out, world units.")]
        [Min(0.01f)] public float FloorOcclusionRadius;

        [Tooltip("Colour the floor occlusion tends to (cool green, not black: shadows are coloured, Art-Spec §1.1).")]
        public Color FloorOcclusionColor;

        [Header("Floor patches and contact shade (A.3c)")]
        [Tooltip("Floor darkening right at a tall-grass bush (contact shade; 0 = none).")]
        [Range(0f, 1f)] public float FloorBushOcclusion;

        [Tooltip("Distance from a bush over which its contact shade fades out, world units.")]
        [Min(0.01f)] public float FloorBushOcclusionRadius;

        [Tooltip("Size of the noise cells that place the grass patches on the floor, world units (patches ≈ this size).")]
        [Min(0.1f)] public float FloorPatchCell;

        [Tooltip("Share of the floor covered by grass patches (0 = sand only).")]
        [Range(0f, 1f)] public float FloorPatchCoverage;

        [Tooltip("Extra patch weight right next to a bush (grass grows around tall grass), fading over FloorBushOcclusionRadius.")]
        [Range(0f, 1f)] public float FloorPatchNearBush;

        [Tooltip("Footprint radius of a round obstacle prop (barrel, stump, boulder) for its contact shade on the floor, world units.")]
        [Min(0f)] public float ObstaclePropRadius;

        [Tooltip("Meadow frame around the arena: width beyond the map edge, world units.")]
        [Min(0f)] public float MeadowMargin;

        [Tooltip("Outer part of the meadow frame fading to MeadowEdge, world units (≤ MeadowMargin).")]
        [Min(0f)] public float MeadowFade;

        [Tooltip("Vertex colour of the meadow's outer edge (multiplies the meadow material; fades into the camera background).")]
        public Color MeadowEdge;

        [Tooltip("Vertex colour of the meadow right at the arena edge (a soft dark border around the play field).")]
        public Color MeadowInner;

        [Tooltip("Width of the darker border of the meadow along the arena edge, world units.")]
        [Min(0f)] public float MeadowInnerWidth;

        [Header("Water (A.3b)")]
        [Tooltip("Water sheet vertices per cell side (≥ 1): finer = smoother shore foam.")]
        [Min(1)] public int WaterSubdivisions;

        [Tooltip("Distance from the shore over which the shore factor (water vertex alpha: foam, shallow colour) fades out, world units.")]
        [Min(0.01f)] public float WaterShoreWidth;

        [Tooltip("Bank of the sunken pond (walls material: planks) at the ground line.")]
        public Color BankTop;

        [Tooltip("Bank colour at the water line (wet, darker).")]
        public Color BankFoot;

        [Header("Wall shadows on the ground (A.3b, MapShadowMask)")]
        [Tooltip("Shadow mask texels per cell side (bilinear: more = sharper shadow edges).")]
        [Min(1)] public int ShadowTexelsPerCell;

        [Tooltip("Strength of the soft wall shadows on the floor, meadow and water (0 = off).")]
        [Range(0f, 1f)] public float ShadowStrength;

        [Tooltip("Softness of the shadow's height test (fraction of the caster height).")]
        [Range(0.02f, 0.5f)] public float ShadowSoftness;

        [Tooltip("Shadow fall per unit of caster height in the camera frame: x = to the screen right, y = towards the viewer (MatchLightRig).")]
        public Vector2 ShadowFall;

        [Tooltip("Relative height of a bush as a shadow caster (0 = casts nothing, 1 = like a wall).")]
        [Range(0f, 1f)] public float BushShadowHeight;

        [Tooltip("Relative height of a breakable crate as a shadow caster.")]
        [Range(0f, 1f)] public float CrateShadowHeight;

        [Tooltip("Relative height of a berry box (round basket) as a shadow caster; 0 = only its own foot shade.")]
        [Range(0f, 1f)] public float BoxShadowHeight;
    }
}
