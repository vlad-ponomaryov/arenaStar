using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Kovach, the blacksmith tank (≈ 1.3 × Theo's height) (A-Roster3D): FBX body, texture and weapon from <c>Art/Source/3D/kovach/source.md</c>, installed like Theo through
    /// <see cref="FbxFighterRecipe"/>. The view of the data fighter <c>"bruno"</c> (D-148: ids are kept, renaming is Q-029).
    /// </summary>
    public static class KovachRecipe
    {
        public const string FighterId = "bruno";
        public const string Hero = "kovach";
        /// <summary>The FBX is ≈ 1.0 tall (source.md); Theo stands 1.5.</summary>
        public const float Scale = 1.95f;

        public static FighterModelRecipe Create() => FbxFighterRecipe.Create(new FbxFighterSpec
        {
            FighterId = FighterId,
            Hero = Hero,
            Scale = Scale,
            AttackStyle = FighterAttackStyle.Swipe,
            UltStyle = FighterAttackStyle.Swipe,
            TrickStyle = FighterTrickStyle.HelmetTap,
            LidRadii = new Vector3(0.031f, 0.022f, 0.010f),
            LidLift = -0.002f,
            LidSegments = 6
        });
    }
}
