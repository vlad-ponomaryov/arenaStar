namespace Arena.Editor.Art.Fighters
{
    /// <summary>How a group of parts becomes triangles (Art-Spec §6.2).</summary>
    public enum FighterGroupMode
    {
        /// <summary>One smooth surface from the distance field (head, torso, tail): no visible seams between the parts.</summary>
        Sculpt = 0,
        /// <summary>Each part is its own primitive mesh (limbs, eyes, props, goggles); paint/cut parts are ignored.</summary>
        Parts = 1
    }
}
