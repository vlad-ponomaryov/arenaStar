using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Elixa, the witch apprentice healer with a cat on her shoulder (Extra) (A-Roster3D): FBX body, texture and weapon from <c>Art/Source/3D/elixa/source.md</c>, installed like Theo through
    /// <see cref="FbxFighterRecipe"/>. The view of the data fighter <c>"kora"</c> (D-148: ids are kept, renaming is Q-029).
    /// </summary>
    public static class ElixaRecipe
    {
        public const string FighterId = "kora";
        public const string Hero = "elixa";
        /// <summary>The FBX is ≈ 1.0 tall (source.md); Theo stands 1.5.</summary>
        public const float Scale = 1.5f;

        public static FighterModelRecipe Create() => FbxFighterRecipe.Create(new FbxFighterSpec
        {
            FighterId = FighterId,
            Hero = Hero,
            Scale = Scale,
            AttackStyle = FighterAttackStyle.Throw,
            UltStyle = FighterAttackStyle.Throw,
            TrickStyle = FighterTrickStyle.Spin,
            LidRadii = new Vector3(0.029f, 0.021f, 0.010f),
            LidLift = -0.002f,
            LidSegments = 8,
            ExtraSpring = new FighterSpring { Bone = FighterBone.Extra, Frequency = 3f, Damping = 0.5f, Amount = 8f }
        });
    }
}
