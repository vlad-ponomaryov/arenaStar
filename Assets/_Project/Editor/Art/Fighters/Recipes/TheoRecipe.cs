using System;
using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Theo, the starting shooter (A-Theo3D): a boy in red-and-gold armour with a big blaster. The body and the blaster come from
    /// FBX (<c>Art/Source/3D/theo/source.md</c>: height 1.0, bones = <see cref="FighterBone"/> names, eyes modelled on EyeL/R,
    /// lids without geometry); the recipe only places them. The view of the fighter id <c>"pix"</c> — renaming the data id is a
    /// separate question; <see cref="PixRecipe"/> stays as the procedural fallback.
    /// </summary>
    public static class TheoRecipe
    {
        public const string FighterId = "pix";
        public const string ModelPath = FighterModelImporter.Folder + "/theo_game.fbx";
        public const string BlasterPath = FighterModelImporter.Folder + "/theo_blaster.fbx";
        /// <summary>The FBX is 1.0 tall; the match fighters stand ≈ 1.5 (Art-Spec §1.4, the hitbox radius 0.45).</summary>
        public const float Scale = 1.5f;

        public static FighterModelRecipe Create()
        {
            // Pivots before the scale; every bone the FBX has is replaced by its head (FighterModelBuilder.ApplySourcePivots),
            // the rest (Tail, Extra) stay as below.
            Vector3 eyeR = new Vector3(0.06f, 0.80f, 0.12f);
            Vector3 eyeL = new Vector3(-eyeR.x, eyeR.y, eyeR.z);
            return new FighterModelRecipe
            {
                FighterId = FighterId,
                Scale = Scale,
                SourceModel = ModelPath,
                SourceProp = BlasterPath,
                Bones = new[]
                {
                    new FighterBoneSpec(FighterBone.Root, FighterBone.Root, Vector3.zero),
                    new FighterBoneSpec(FighterBone.Body, FighterBone.Root, new Vector3(0f, 0.375f, 0f)),
                    new FighterBoneSpec(FighterBone.Head, FighterBone.Body, new Vector3(0f, 0.665f, 0f)),
                    new FighterBoneSpec(FighterBone.ArmL, FighterBone.Body, new Vector3(-0.135f, 0.62f, 0f)),
                    new FighterBoneSpec(FighterBone.ArmR, FighterBone.Body, new Vector3(0.135f, 0.62f, 0f)),
                    new FighterBoneSpec(FighterBone.LegL, FighterBone.Root, new Vector3(-0.06f, 0.375f, 0f)),
                    new FighterBoneSpec(FighterBone.LegR, FighterBone.Root, new Vector3(0.06f, 0.375f, 0f)),
                    new FighterBoneSpec(FighterBone.Tail, FighterBone.Body, new Vector3(0f, 0.40f, -0.10f)),
                    new FighterBoneSpec(FighterBone.Prop, FighterBone.Body, new Vector3(0.28f, 0.351f, 0.049f)),
                    new FighterBoneSpec(FighterBone.Extra, FighterBone.Head, new Vector3(0f, 1.0f, 0f)),
                    new FighterBoneSpec(FighterBone.EyeL, FighterBone.Head, eyeL),
                    new FighterBoneSpec(FighterBone.EyeR, FighterBone.Head, eyeR),
                    new FighterBoneSpec(FighterBone.LidL, FighterBone.Head, eyeL),
                    new FighterBoneSpec(FighterBone.LidR, FighterBone.Head, eyeR)
                },
                // Eyes are in the FBX (on EyeL/R); blinking via lids on LidL/R is for later (A-Theo3D-unity.md).
                Groups = Array.Empty<FighterGroupSpec>(),
                AttackStyle = FighterAttackStyle.Shoot,
                UltStyle = FighterAttackStyle.Shoot,
                TrickStyle = FighterTrickStyle.Roll,
                Springs = new[]
                {
                    new FighterSpring { Bone = FighterBone.Head, Frequency = 3.5f, Damping = 0.55f, Amount = 6f }
                }
            };
        }
    }
}
