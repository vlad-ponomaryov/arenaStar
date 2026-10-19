namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// Primitive shapes of the procedural models (Art-Spec §6.2), the same in <see cref="ProcMesh"/> (triangles) and
    /// <see cref="SdfPrimitive"/> (distance field). Sizes are in the primitive's local space before its scale.
    /// </summary>
    public enum PartShape
    {
        /// <summary>Size = radii.</summary>
        Ellipsoid = 0,
        /// <summary>Along local +Y from the origin: Size.x = bottom radius, Size.y = length, Size.z = top radius (capsule if equal).</summary>
        RoundCone = 1,
        /// <summary>Size = half extents, Radius = corner radius.</summary>
        RoundedBox = 2,
        /// <summary>In the local XZ plane: Size.x = ring radius, Radius = tube radius (elliptic ring — by the part's scale).</summary>
        Torus = 3
    }
}
