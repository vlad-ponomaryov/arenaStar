namespace Arena.Gameplay.View
{
    /// <summary>Trick clip of a fighter (Art-Spec §3.1); written by the model builder from the recipe. New styles go to the end.</summary>
    public enum FighterTrickStyle
    {
        None = 0,
        /// <summary>Forward roll (Pix).</summary>
        Roll = 1,
        /// <summary>Knocks on the helmet (Bruno).</summary>
        HelmetTap = 2,
        Spin = 3,
        Hop = 4,
        /// <summary>Vanishes in a puff (Vex).</summary>
        Vanish = 5
    }
}
