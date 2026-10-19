namespace Arena.Editor.Art.Fighters
{
    /// <summary>
    /// Level of detail a fighter recipe is built at (D-122): <see cref="Game"/> — the match model within the Art-Spec §6.2 budget;
    /// <see cref="Hero"/> — the showcase model for menus, fighter select and results (same recipe, finer sculpt, ≈ 15–30k triangles).
    /// </summary>
    public enum FighterModelQuality
    {
        Game = 0,
        Hero = 1
    }
}
