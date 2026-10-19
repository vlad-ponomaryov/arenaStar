using Arena.Gameplay.View;

namespace Arena.Editor.Art.Fighters
{
    /// <summary>Parts of one bone built together (Art-Spec §6.2): sculpted into one surface or as separate primitives.</summary>
    public sealed class FighterGroupSpec
    {
        public string Name;
        public FighterBone Bone;
        public FighterGroupMode Mode;
        /// <summary>Smooth-union radius of a sculpted group, units (0.04–0.12).</summary>
        public float Blend;
        /// <summary>Triangle budget of the group (Art-Spec §6.2); a sculpted group picks its grid step to fit it.</summary>
        public int Budget;
        /// <summary>Steps around for parts that do not set their own.</summary>
        public int Segments = 10;
        public FighterPartSpec[] Parts;

        public static FighterGroupSpec Sculpt(string name, FighterBone bone, float blend, int budget, params FighterPartSpec[] parts) =>
            new FighterGroupSpec { Name = name, Bone = bone, Mode = FighterGroupMode.Sculpt, Blend = blend, Budget = budget, Parts = parts };

        public static FighterGroupSpec Pieces(string name, FighterBone bone, int budget, int segments, params FighterPartSpec[] parts) =>
            new FighterGroupSpec { Name = name, Bone = bone, Mode = FighterGroupMode.Parts, Budget = budget, Segments = segments, Parts = parts };
    }
}
