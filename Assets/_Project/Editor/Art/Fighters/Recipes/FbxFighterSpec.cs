using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>One roster hero's data for <see cref="FbxFighterRecipe.Create"/> (A-Roster3D).</summary>
    public sealed class FbxFighterSpec
    {
        public const int DefaultLidSegments = 8;
        public string FighterId;
        /// <summary>File name prefix in <see cref="FighterModelImporter.Folder"/> and the texture folder (e.g. <c>gala</c>).</summary>
        public string Hero;
        public float Scale;
        public FighterAttackStyle AttackStyle;
        public FighterAttackStyle UltStyle;
        public FighterTrickStyle TrickStyle;
        /// <summary>Half-sizes of one closed eyelid (across, up, depth), before the scale.</summary>
        public Vector3 LidRadii;
        /// <summary>Shift of the lid along the face normal from the FBX eye bone head (the bone head sits on the eye surface).</summary>
        public float LidLift;
        public int LidSegments = DefaultLidSegments;
        /// <summary>Extra spring (Elixa's cat, Lark's drone); null = only the head spring.</summary>
        public FighterSpring? ExtraSpring;
    }
}
