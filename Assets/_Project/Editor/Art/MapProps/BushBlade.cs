using UnityEngine;

namespace Arena.Editor.Art.MapProps
{
    /// <summary>
    /// One grass blade of a bush cell (A.3d): where it grows and how it stands. <see cref="EnvMeshBuilder.BushBladeLayout"/> lays
    /// them out; the mesh builder turns each into a lancet of two segments and a tip.
    /// </summary>
    public struct BushBlade
    {
        /// <summary>Column and row in the cell's blade grid (0 = west / south).</summary>
        public int Column, Row;

        /// <summary>Root on the ground (y = 0), cell space (cell centre at the origin).</summary>
        public Vector3 Root;

        /// <summary>Height of the tip above the ground.</summary>
        public float Height;

        /// <summary>Horizontal offset of the tip from the root (lean).</summary>
        public Vector3 Lean;

        /// <summary>Unit normal of the blade's plane (horizontal, roughly towards −Z).</summary>
        public Vector3 Facing;

        /// <summary>Width at the root.</summary>
        public float Width;

        /// <summary>Darkening of an inner blade (1 = at the open edge of the mass, less = deeper inside).</summary>
        public float Shade;

        /// <summary>True for a blade next to a shared side: same in every variant and mask with that side shared (no seam).</summary>
        public bool EdgeLocked;
    }
}
