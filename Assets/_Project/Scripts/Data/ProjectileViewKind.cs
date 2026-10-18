namespace Arena.Data
{
    /// <summary>How the view draws the projectiles of an ability (view only; the simulation ignores it). Append at the end.</summary>
    public enum ProjectileViewKind : byte
    {
        /// <summary>Sphere of the hit diameter (drops, cups, cones).</summary>
        Sphere = 0,

        /// <summary>Rolling log lying across the flight direction (Chomp's ult).</summary>
        Log = 1
    }
}
