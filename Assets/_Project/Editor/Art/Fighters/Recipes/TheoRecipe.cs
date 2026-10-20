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

        // Eyelids (A-TheoAnim AN4): the eyes are painted, so each lid is a flattened half-buried ellipsoid over the painted eye on
        // LidL/R (scale 0 at rest = collapsed into the eye centre, hidden; 1 = closed) with a dark lash line across its lower half.
        // The eye centres are the FBX bone heads of EyeL/R (≈ 8 mm under the face); the face there faces forward, up and outwards.
        // The lid shows the texture's skin from just under the eye (UV probe); sizes before the scale, worked out on close-ups.
        // The left eye is painted on the visor lens (≈ 2 cm in front of the eye centre): its lid lies on the lens and shows the
        // lens orange, so the closed eye reads as behind the glass.
        public static readonly Vector3 EyeCentreR = new Vector3(0.0469f, 0.8007f, 0.1009f);
        private static readonly Vector3 EyeFacing = new Vector3(0.15f, 0.28f, 0.95f);
        private static readonly Vector3 LidRadii = new Vector3(0.031f, 0.022f, 0.010f);
        private const float LidLift = 0.006f;
        private static readonly Vector3 LashRadii = new Vector3(0.032f, 0.0032f, 0.009f);
        private const float LashLift = 0.008f;
        private const float LashDrop = 0.004f;
        private static readonly Vector3 SkinProbeOffset = new Vector3(0f, -0.05f, 0.009f);
        private const float VisorLensLift = 0.017f;
        private static readonly Vector3 VisorFacing = new Vector3(-0.15f, 0.12f, 0.98f);
        private static readonly Vector3 VisorProbe = new Vector3(-0.055f, 0.776f, 0.122f);
        private const string LidColor = "#FFFFFF";
        private const string LashColor = "#2B1D12";
        private const int LidSegments = 8;
        private const int LashSegments = 4;
        private const int LidBudget = 48;
        private const int LashBudget = 8;

        /// <summary>The lid and lash groups of one eye (<paramref name="side"/> = +1 right, −1 left; the left one on the visor lens).</summary>
        private static FighterGroupSpec[] Eyelid(FighterBone bone, float side)
        {
            bool visor = side < 0f;
            var eye = new Vector3(EyeCentreR.x * side, EyeCentreR.y, EyeCentreR.z);
            Vector3 facing = visor ? VisorFacing.normalized : new Vector3(EyeFacing.x * side, EyeFacing.y, EyeFacing.z).normalized;
            Vector3 centre = visor ? eye + Vector3.forward * VisorLensLift : eye;
            Quaternion turn = Quaternion.LookRotation(facing, Vector3.up);
            Vector3 up = turn * Vector3.up;
            string name = bone == FighterBone.LidL ? "lidL" : "lidR";
            return new[]
            {
                new FighterGroupSpec
                {
                    Name = name, Bone = bone, Mode = FighterGroupMode.Parts, Budget = LidBudget, Segments = LidSegments,
                    Parts = new[] { FighterPartSpec.Ellipsoid(centre + facing * LidLift, LidRadii, LidColor).Oriented(turn) },
                    UvProbe = visor ? VisorProbe : eye + SkinProbeOffset
                },
                FighterGroupSpec.Pieces(name + "Lash", bone, LashBudget, LashSegments,
                    FighterPartSpec.Ellipsoid(centre + facing * LashLift - up * LashDrop, LashRadii, LashColor).Oriented(turn))
            };
        }

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
                // Eyes are painted in the texture (EyeL/R carry no weights); the lids are procedural groups on LidL/R.
                Groups = Concat(Eyelid(FighterBone.LidL, -1f), Eyelid(FighterBone.LidR, 1f)),
                AttackStyle = FighterAttackStyle.Shoot,
                UltStyle = FighterAttackStyle.Shoot,
                TrickStyle = FighterTrickStyle.Roll,
                Springs = new[]
                {
                    new FighterSpring { Bone = FighterBone.Head, Frequency = 3.5f, Damping = 0.55f, Amount = 6f }
                }
            };
        }

        private static FighterGroupSpec[] Concat(FighterGroupSpec[] a, FighterGroupSpec[] b)
        {
            var all = new FighterGroupSpec[a.Length + b.Length];
            Array.Copy(a, all, a.Length);
            Array.Copy(b, 0, all, a.Length, b.Length);
            return all;
        }
    }
}
