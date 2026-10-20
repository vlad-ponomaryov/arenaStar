using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Lark, the scout sniper with a drone over her shoulder (Extra) (A-Roster3D): FBX body, texture and weapon from <c>Art/Source/3D/lark/source.md</c>, installed like Theo through
    /// <see cref="FbxFighterRecipe"/>. The view of the data fighter <c>"nova"</c> (D-148: ids are kept, renaming is Q-029).
    /// </summary>
    public static class LarkRecipe
    {
        public const string FighterId = "nova";
        public const string Hero = "lark";
        /// <summary>The FBX is ≈ 1.0 tall (source.md); Theo stands 1.5.</summary>
        public const float Scale = 1.5f;

        public static FighterModelRecipe Create() => FbxFighterRecipe.Create(new FbxFighterSpec
        {
            FighterId = FighterId,
            Hero = Hero,
            Scale = Scale,
            AttackStyle = FighterAttackStyle.Shoot,
            UltStyle = FighterAttackStyle.Shoot,
            TrickStyle = FighterTrickStyle.Hop,
            LidRadii = new Vector3(0.036f, 0.025f, 0.010f),
            LidLift = -0.002f,
            LidSegments = 6,
            ExtraSpring = new FighterSpring { Bone = FighterBone.Extra, Frequency = 2.5f, Damping = 0.4f, Amount = 10f }
        });
    }
}
