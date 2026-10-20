using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Zip, the parkour assassin with a hookshot (A-Roster3D): FBX body, texture and weapon from <c>Art/Source/3D/zip/source.md</c>, installed like Theo through
    /// <see cref="FbxFighterRecipe"/>. The view of the data fighter <c>"vex"</c> (D-148: ids are kept, renaming is Q-029).
    /// </summary>
    public static class ZipRecipe
    {
        public const string FighterId = "vex";
        public const string Hero = "zip";
        /// <summary>The FBX is ≈ 1.0 tall (source.md); Theo stands 1.5.</summary>
        public const float Scale = 1.5f;

        public static FighterModelRecipe Create() => FbxFighterRecipe.Create(new FbxFighterSpec
        {
            FighterId = FighterId,
            Hero = Hero,
            Scale = Scale,
            AttackStyle = FighterAttackStyle.Claw,
            UltStyle = FighterAttackStyle.Shoot,
            TrickStyle = FighterTrickStyle.Vanish,
            LidRadii = new Vector3(0.028f, 0.020f, 0.010f),
            LidLift = -0.002f,
            LidSegments = 8
        });
    }
}
