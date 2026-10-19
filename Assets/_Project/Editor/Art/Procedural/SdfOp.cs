namespace Arena.Editor.Art.Procedural
{
    /// <summary>What a primitive does in a sculpted group (<see cref="SdfSculpt"/>).</summary>
    public enum SdfOp
    {
        /// <summary>Adds volume, smoothly blended with the others.</summary>
        Union = 0,
        /// <summary>Carves volume out (eye sockets), smoothly.</summary>
        Subtract = 1,
        /// <summary>No volume: recolours the surface inside it (muzzle, ear tips, belly patch).</summary>
        Paint = 2
    }
}
