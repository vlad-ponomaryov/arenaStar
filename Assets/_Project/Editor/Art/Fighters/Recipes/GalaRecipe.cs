using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Gala, the talent-show star thrower (A-Roster3D): FBX body, texture and weapon from <c>Art/Source/3D/gala/source.md</c>, installed like Theo through
    /// <see cref="FbxFighterRecipe"/>. The view of the data fighter <c>"chomp"</c> (D-148: ids are kept, renaming is Q-029).
    /// </summary>
    public static class GalaRecipe
    {
        public const string FighterId = "chomp";
        public const string Hero = "gala";
        /// <summary>The FBX is ≈ 1.0 tall (source.md); Theo stands 1.5.</summary>
        public const float Scale = 1.5f;

        public static FighterModelRecipe Create() => FbxFighterRecipe.Create(new FbxFighterSpec
        {
            FighterId = FighterId,
            Hero = Hero,
            Scale = Scale,
            AttackStyle = FighterAttackStyle.Lob,
            UltStyle = FighterAttackStyle.Lob,
            TrickStyle = FighterTrickStyle.Spin,
            LidRadii = new Vector3(0.030f, 0.024f, 0.010f),
            LidLift = -0.002f,
            LidSegments = 8
        });
    }
}
