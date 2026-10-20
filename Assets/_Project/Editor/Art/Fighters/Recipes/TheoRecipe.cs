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
        public const string ModelPath = FighterModelImporter.Folder + "/theo_v3_game.fbx";
        public const string HeroModelPath = FighterModelImporter.Folder + "/theo_v3_show.fbx";
        /// <summary>RGB = colour with baked AO and highlights (sRGB), A = gloss; shared by both FBX (same UVs).</summary>
        public const string BaseMapPath = FighterModelImporter.TextureFolder + "/theo_v3_tex.png";
        public const string BlasterPath = FighterModelImporter.Folder + "/theo_blaster.fbx";
        /// <summary>The FBX is 1.0 tall; the match fighters stand ≈ 1.5 (Art-Spec §1.4, the hitbox radius 0.45).</summary>
        public const float Scale = 1.5f;

        // A-TheoPolish: the blaster in both hands at the belt, barrel forward (+Z) and a little down. Rest pose in model space
        // (Euler degrees): the right arm brings the grip in front of the right hip (hand ≈ (0.10, 0.44, 0.19) before the scale),
        // the left arm crosses to the side of the receiver; worked out on Blender previews of the FBX (A-TheoPolish §1).
        public static readonly Vector3 ArmRPose = new Vector3(-44.5f, -17.8f, -20.0f);
        public static readonly Vector3 ArmLPose = new Vector3(-67.3f, 18.2f, 34.9f);
        /// <summary>The blaster: barrel 8° towards the body's middle line and 4° down.</summary>
        public static readonly Vector3 BlasterPose = new Vector3(4f, -8f, 0f);
        /// <summary>End of the barrel in the blaster FBX (origin = the grip, +Z = the barrel), before the scale.</summary>
        public static readonly Vector3 BlasterMuzzle = new Vector3(0f, 0.107f, 0.422f);

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
                SourceModelHero = HeroModelPath,
                BaseMap = BaseMapPath,
                SourceProp = BlasterPath,
                PropMuzzle = BlasterMuzzle,
                RestPose = new[]
                {
                    new FighterBonePose(FighterBone.ArmR, ArmRPose),
                    new FighterBonePose(FighterBone.ArmL, ArmLPose),
                    new FighterBonePose(FighterBone.Prop, BlasterPose)
                },
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
                    // Child of the right arm: the blaster stays in the hand when the arm moves (recoil, run swing).
                    new FighterBoneSpec(FighterBone.Prop, FighterBone.ArmR, new Vector3(0.28f, 0.351f, 0.049f)),
                    new FighterBoneSpec(FighterBone.Extra, FighterBone.Head, new Vector3(0f, 1.0f, 0f)),
                    new FighterBoneSpec(FighterBone.EyeL, FighterBone.Head, eyeL),
                    new FighterBoneSpec(FighterBone.EyeR, FighterBone.Head, eyeR),
                    new FighterBoneSpec(FighterBone.LidL, FighterBone.Head, eyeL),
                    new FighterBoneSpec(FighterBone.LidR, FighterBone.Head, eyeR)
                },
                // Eyes are painted in the texture (EyeL/R, LidL/R carry no weights); blinking via lids on LidL/R is for later.
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
