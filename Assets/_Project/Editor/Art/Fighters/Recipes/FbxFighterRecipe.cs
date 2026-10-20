using System;
using Arena.Gameplay.View;
using UnityEngine;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Shared recipe of the roster heroes made like Theo v3 (A-Roster3D): body <c>&lt;hero&gt;_game.fbx</c> with its texture
    /// <c>&lt;hero&gt;_tex.png</c>, held weapon <c>&lt;hero&gt;_weapon.fbx</c> on Prop (a child of the right arm), the muzzle read from the
    /// weapon's <c>Muzzle</c> empty, procedural eyelids on LidL/R over the painted eyes (like <see cref="TheoRecipe"/>, without the
    /// visor). The FBX already holds the weapon in the right hand (Prop = the hand), so the bind pose is the rest pose. Bones the FBX
    /// has take its heads as pivots (<see cref="FighterModelBuilder.ApplySourcePivots"/>); the others use the defaults below.
    /// Each hero's numbers live in its own recipe file.
    /// </summary>
    public static class FbxFighterRecipe
    {
        public const string MuzzleEmpty = "Muzzle";
                private const int LashSegments = 4;
        private const int LashBudget = 8;
        private const string LidColor = "#FFFFFF";
        private const string LashColor = "#2B1D12";
        /// <summary>Forward, a little up and outwards — the face around the eyes (as Theo's).</summary>
        private static readonly Vector3 EyeFacing = new Vector3(0.15f, 0.28f, 0.95f);
        private const float LashThickness = 0.0032f;
        private const float LashLiftExtra = 0.002f;
        private const float LashDropShare = 0.18f;
        /// <summary>Where the lid takes its colour from: the skin just under the eye.</summary>
        private static readonly Vector3 SkinProbeOffset = new Vector3(0f, -0.05f, 0.009f);

        // Default pivots (before the scale) of the bones the FBX may not have.
        private static readonly Vector3 TailPivot = new Vector3(0f, 0.40f, -0.10f);
        private static readonly Vector3 ExtraPivot = new Vector3(0f, 1.0f, 0f);
        private static readonly FighterSpring HeadSpring = new FighterSpring { Bone = FighterBone.Head, Frequency = 3.5f, Damping = 0.55f, Amount = 6f };

        public static string ModelPath(string hero) => FighterModelImporter.Folder + "/" + hero + "_game.fbx";
        public static string WeaponPath(string hero) => FighterModelImporter.Folder + "/" + hero + "_weapon.fbx";
        public static string BaseMapPath(string hero) => FighterModelImporter.TextureFolder + "/" + hero + "_tex.png";

        public static FighterModelRecipe Create(FbxFighterSpec s)
        {
            string model = ModelPath(s.Hero);
            string weapon = WeaponPath(s.Hero);
            Vector3?[] pivots = FighterModelSource.BonePivots(model);
            Vector3 eyeR = pivots[(int)FighterBone.EyeR] ?? throw new InvalidOperationException(model + " has no EyeR bone");
            Vector3 eyeL = pivots[(int)FighterBone.EyeL] ?? new Vector3(-eyeR.x, eyeR.y, eyeR.z);
            Vector3 hand = pivots[(int)FighterBone.Prop] ?? throw new InvalidOperationException(model + " has no Prop bone");
            // A melee weapon (Kovach's hammer) has no Muzzle empty: the muzzle stays at the grip.
            Vector3 muzzle = FighterModelSource.PropPoint(weapon, MuzzleEmpty) ?? Vector3.zero;
            var lids = new FighterGroupSpec[4];
            Eyelid(s, FighterBone.LidL, eyeL, eyeL.x < 0f ? -1f : 1f).CopyTo(lids, 0);
            Eyelid(s, FighterBone.LidR, eyeR, eyeR.x < 0f ? -1f : 1f).CopyTo(lids, 2);
            return new FighterModelRecipe
            {
                FighterId = s.FighterId,
                Scale = s.Scale,
                SourceModel = model,
                BaseMap = BaseMapPath(s.Hero),
                SourceProp = weapon,
                PropMuzzle = muzzle,
                Bones = new[]
                {
                    // Pivots of the bones the FBX has are replaced by its bone heads; these are the fallbacks.
                    new FighterBoneSpec(FighterBone.Root, FighterBone.Root, Vector3.zero),
                    new FighterBoneSpec(FighterBone.Body, FighterBone.Root, new Vector3(0f, 0.4f, 0f)),
                    new FighterBoneSpec(FighterBone.Head, FighterBone.Body, new Vector3(0f, 0.67f, 0f)),
                    new FighterBoneSpec(FighterBone.ArmL, FighterBone.Body, new Vector3(-0.13f, 0.6f, 0f)),
                    new FighterBoneSpec(FighterBone.ArmR, FighterBone.Body, new Vector3(0.13f, 0.6f, 0f)),
                    new FighterBoneSpec(FighterBone.LegL, FighterBone.Root, new Vector3(-0.06f, 0.37f, 0f)),
                    new FighterBoneSpec(FighterBone.LegR, FighterBone.Root, new Vector3(0.06f, 0.37f, 0f)),
                    new FighterBoneSpec(FighterBone.Tail, FighterBone.Body, TailPivot),
                    // Child of the right arm: the weapon stays in the hand when the arm moves.
                    new FighterBoneSpec(FighterBone.Prop, FighterBone.ArmR, hand),
                    new FighterBoneSpec(FighterBone.Extra, FighterBone.Body, ExtraPivot),
                    new FighterBoneSpec(FighterBone.EyeL, FighterBone.Head, eyeL),
                    new FighterBoneSpec(FighterBone.EyeR, FighterBone.Head, eyeR),
                    new FighterBoneSpec(FighterBone.LidL, FighterBone.Head, eyeL),
                    new FighterBoneSpec(FighterBone.LidR, FighterBone.Head, eyeR)
                },
                Groups = lids,
                AttackStyle = s.AttackStyle,
                UltStyle = s.UltStyle,
                TrickStyle = s.TrickStyle,
                Springs = s.ExtraSpring.HasValue ? new[] { HeadSpring, s.ExtraSpring.Value } : new[] { HeadSpring }
            };
        }

        /// <summary>A flattened half-buried ellipsoid over the painted eye (scale 0 at rest) with a dark lash line, as Theo's.</summary>
        private static FighterGroupSpec[] Eyelid(FbxFighterSpec s, FighterBone bone, Vector3 eye, float side)
        {
            Vector3 facing = new Vector3(EyeFacing.x * side, EyeFacing.y, EyeFacing.z).normalized;
            Quaternion turn = Quaternion.LookRotation(facing, Vector3.up);
            Vector3 up = turn * Vector3.up;
            string name = bone == FighterBone.LidL ? "lidL" : "lidR";
            var lash = new Vector3(s.LidRadii.x * 1.03f, LashThickness, s.LidRadii.z * 0.9f);
            int lidTriangles = s.LidSegments * (s.LidSegments / 2 - 1) * 2;
            return new[]
            {
                new FighterGroupSpec
                {
                    Name = name, Bone = bone, Mode = FighterGroupMode.Parts, Budget = lidTriangles, Segments = s.LidSegments,
                    Parts = new[] { FighterPartSpec.Ellipsoid(eye + facing * s.LidLift, s.LidRadii, LidColor).Oriented(turn) },
                    UvProbe = eye + SkinProbeOffset
                },
                FighterGroupSpec.Pieces(name + "Lash", bone, LashBudget, LashSegments,
                    FighterPartSpec.Ellipsoid(eye + facing * (s.LidLift + LashLiftExtra) - up * (s.LidRadii.y * LashDropShare), lash, LashColor)
                        .Oriented(turn))
            };
        }
    }
}
