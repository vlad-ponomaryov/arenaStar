using Arena.Gameplay.View;
using UnityEngine;
using P = Arena.Editor.Art.Fighters.FighterPartSpec;

namespace Arena.Editor.Art.Fighters.Recipes
{
    /// <summary>
    /// Pix the fox, the shooter (Art-Spec §1.4; concept — Art/Source/Flow/2026-10-19_pix-fox/001.png): big pointed ears with dark
    /// tips, white cheeks and muzzle, yellow goggles on the forehead on a dark strap, white chest, dark paws, a huge tail with a
    /// white tip curled to the side and up, turquoise water blaster held with both paws. Quality reference for the other recipes.
    /// </summary>
    public static class PixRecipe
    {
        // Palette (Art-Spec §1.3–1.4).
        private const string Fur = "#F07A2A";
        private const string Cream = "#FFF3E0";
        private const string Dark = "#4A2A1A";
        private const string InnerEarColor = "#FFD9BC";
        private const string Nose = "#2A1A12";
        private const string Goggles = "#FFD23F";
        private const string Lens = "#FFF1A0";
        private const string Strap = "#34363C";
        private const string Blaster = "#2EC4C9";
        private const string BlasterDark = "#1F9AA0";
        private const string Tank = "#BFF4FF";
        private const string Nozzle = "#FFB43C";
        private const string EyeWhite = "#FFFFFF";
        private const string Pupil = "#1E1A18";

        // Key points, model space (ground y = 0, facing +Z, +X = Pix's right).
        private static readonly Vector3 HeadCenter = new Vector3(0f, 1.06f, 0f);
        private static readonly Vector3 HeadRadii = new Vector3(0.305f, 0.275f, 0.27f);
        private static readonly Vector3 EyeCenterR = new Vector3(0.118f, 1.075f, 0.232f);
        private const float EyeYaw = 22f;   // eyes turned out to the sides
        private const float EyePitch = -18f; // and up towards the camera (Art-Spec §1.1.2)

        public static FighterModelRecipe Create()
        {
            return new FighterModelRecipe
            {
                FighterId = "pix",
                Scale = 1f,
                Bones = new[]
                {
                    new FighterBoneSpec(FighterBone.Root, FighterBone.Root, Vector3.zero),
                    new FighterBoneSpec(FighterBone.Body, FighterBone.Root, new Vector3(0f, 0.30f, 0f)),
                    new FighterBoneSpec(FighterBone.Head, FighterBone.Body, new Vector3(0f, 0.80f, 0f)),
                    new FighterBoneSpec(FighterBone.ArmL, FighterBone.Body, new Vector3(-0.21f, 0.70f, 0.02f)),
                    new FighterBoneSpec(FighterBone.ArmR, FighterBone.Body, new Vector3(0.21f, 0.70f, 0.02f)),
                    new FighterBoneSpec(FighterBone.LegL, FighterBone.Root, new Vector3(-0.13f, 0.22f, 0.01f)),
                    new FighterBoneSpec(FighterBone.LegR, FighterBone.Root, new Vector3(0.13f, 0.22f, 0.01f)),
                    new FighterBoneSpec(FighterBone.Tail, FighterBone.Body, new Vector3(0.05f, 0.30f, -0.22f)),
                    new FighterBoneSpec(FighterBone.Prop, FighterBone.Body, new Vector3(0f, 0.55f, 0.34f)),
                    new FighterBoneSpec(FighterBone.Extra, FighterBone.Head, new Vector3(0f, 1.32f, 0f)),
                    new FighterBoneSpec(FighterBone.EyeL, FighterBone.Head, Mirror(EyeCenterR)),
                    new FighterBoneSpec(FighterBone.EyeR, FighterBone.Head, EyeCenterR),
                    new FighterBoneSpec(FighterBone.LidL, FighterBone.Head, Mirror(EyeCenterR)),
                    new FighterBoneSpec(FighterBone.LidR, FighterBone.Head, EyeCenterR)
                },
                Groups = new[]
                {
                    Head(),
                    FighterGroupSpec.Pieces("headgear", FighterBone.Head, 400, 10, HeadGear()),
                    Body(),
                    Tail(),
                    FighterGroupSpec.Sculpt("armL", FighterBone.ArmL, 0.04f, 150, Arm(-1f)),
                    FighterGroupSpec.Sculpt("armR", FighterBone.ArmR, 0.04f, 150, Arm(1f)),
                    FighterGroupSpec.Sculpt("legL", FighterBone.LegL, 0.05f, 150, Leg(-1f)),
                    FighterGroupSpec.Sculpt("legR", FighterBone.LegR, 0.05f, 150, Leg(1f)),
                    FighterGroupSpec.Pieces("blaster", FighterBone.Prop, 500, 10, BlasterParts()),
                    FighterGroupSpec.Pieces("eyeL", FighterBone.EyeL, 130, 8, Eye(-1f)),
                    FighterGroupSpec.Pieces("eyeR", FighterBone.EyeR, 130, 8, Eye(1f)),
                    FighterGroupSpec.Pieces("lidL", FighterBone.LidL, 50, 8, Lid(-1f)),
                    FighterGroupSpec.Pieces("lidR", FighterBone.LidR, 50, 8, Lid(1f))
                },
                AttackStyle = FighterAttackStyle.Shoot,
                UltStyle = FighterAttackStyle.Shoot,
                TrickStyle = FighterTrickStyle.Roll,
                Springs = new[]
                {
                    new FighterSpring { Bone = FighterBone.Tail, Frequency = 2.2f, Damping = 0.35f, Amount = 22f },
                    new FighterSpring { Bone = FighterBone.Head, Frequency = 3.5f, Damping = 0.55f, Amount = 6f }
                }
            };
        }

        /// <summary>
        /// Round soft head (A.1b2: a vinyl-toy ball, not a box) with muzzle, cheek fluff and ears in one surface (no seams); face and
        /// ear colours painted on. The ears sit on top with narrow bases and the cheeks low and forward, so the outline stays round.
        /// </summary>
        private static FighterGroupSpec Head()
        {
            return FighterGroupSpec.Sculpt("head", FighterBone.Head, 0.07f, 1060,
                P.Ellipsoid(HeadCenter, HeadRadii, Fur),
                // Cheek fluff: soft cream pads low on the face ending in short rounded points out to the sides.
                P.Ellipsoid(new Vector3(0.15f, 0.95f, 0.09f), new Vector3(0.17f, 0.105f, 0.15f), Cream).Rotated(new Vector3(0f, 0f, -12f)),
                P.Ellipsoid(new Vector3(-0.15f, 0.95f, 0.09f), new Vector3(0.17f, 0.105f, 0.15f), Cream).Rotated(new Vector3(0f, 0f, 12f)),
                P.Cone(new Vector3(0.24f, 0.93f, 0.06f), new Vector3(0.375f, 0.875f, 0.03f), 0.075f, 0.035f, Cream),
                P.Cone(new Vector3(-0.24f, 0.93f, 0.06f), new Vector3(-0.375f, 0.875f, 0.03f), 0.075f, 0.035f, Cream),
                // Short soft muzzle.
                P.Ellipsoid(new Vector3(0f, 0.955f, 0.21f), new Vector3(0.115f, 0.08f, 0.13f), Cream),
                // Ears ≈ 0.45 of the head height, flattened front to back, tilted out.
                Ear(1f), Ear(-1f),
                // Paint: white lower face, cream inside the ears, dark ear tips (≈ 1/3 of the ear).
                P.Ellipsoid(new Vector3(0f, 0.89f, 0.15f), new Vector3(0.27f, 0.11f, 0.22f), Cream).AsPaint(),
                InnerEar(1f), InnerEar(-1f),
                P.Sphere(new Vector3(0.30f, 1.60f, -0.05f), 0.14f, Dark).AsPaint(),
                P.Sphere(new Vector3(-0.30f, 1.60f, -0.05f), 0.14f, Dark).AsPaint());
        }

        private static P Ear(float side) =>
            P.Cone(new Vector3(0.11f * side, 1.24f, -0.03f), new Vector3(0.30f * side, 1.60f, -0.06f), 0.10f, 0.035f, Fur)
                .Squash(new Vector3(1f, 1f, 0.6f));

        private static P InnerEar(float side) =>
            P.Cone(new Vector3(0.14f * side, 1.31f, 0.04f), new Vector3(0.265f * side, 1.51f, 0.01f), 0.078f, 0.022f, InnerEarColor)
                .Squash(new Vector3(1f, 1f, 0.5f)).AsPaint();

        /// <summary>Goggles on the forehead (two yellow rims with lenses) on a dark strap round the head, and the nose.</summary>
        private static P[] HeadGear()
        {
            var parts = new System.Collections.Generic.List<P>();
            for (int s = -1; s <= 1; s += 2)
            {
                var center = new Vector3(0.125f * s, 1.215f, 0.20f);
                Vector3 normal = new Vector3(0.30f * s, 0.80f, 0.62f).normalized;
                parts.Add(P.Ring(center, normal, 0.078f, 0.028f, Goggles).Glossy(0.6f).WithSegments(10));
                parts.Add(P.Ellipsoid(center - normal * 0.004f, new Vector3(0.076f, 0.02f, 0.076f), Lens)
                    .Oriented(Quaternion.FromToRotation(Vector3.up, normal)).Glossy(1f).WithSegments(8));
            }
            // Strap: an elliptic ring round the head, higher at the front (under the goggles), lower at the back.
            parts.Add(P.Ring(new Vector3(0f, 1.18f, -0.005f), Vector3.up, 0.28f, 0.022f, Strap)
                .Squash(new Vector3(1f, 1f, 0.93f)).Rotated(new Vector3(-12f, 0f, 0f)).WithSegments(12));
            parts.Add(P.Ellipsoid(new Vector3(0f, 0.975f, 0.345f), new Vector3(0.048f, 0.036f, 0.036f), Nose).Glossy(0.8f)
                .WithSegments(6));
            return parts.ToArray();
        }

        /// <summary>Pear-shaped torso with thighs, white chest-and-belly patch.</summary>
        private static FighterGroupSpec Body()
        {
            return FighterGroupSpec.Sculpt("body", FighterBone.Body, 0.10f, 860,
                P.Ellipsoid(new Vector3(0f, 0.40f, 0f), new Vector3(0.26f, 0.25f, 0.23f), Fur),
                P.Ellipsoid(new Vector3(0f, 0.64f, 0.005f), new Vector3(0.195f, 0.21f, 0.175f), Fur),
                P.Ellipsoid(new Vector3(0.13f, 0.22f, 0.03f), new Vector3(0.12f, 0.12f, 0.13f), Fur),
                P.Ellipsoid(new Vector3(-0.13f, 0.22f, 0.03f), new Vector3(0.12f, 0.12f, 0.13f), Fur),
                P.Ellipsoid(new Vector3(0f, 0.48f, 0.16f), new Vector3(0.165f, 0.30f, 0.14f), Cream).AsPaint());
        }

        /// <summary>
        /// Huge fluffy tail: one smooth tapering tube (round cones through the key points, A.1b2) from the back out to Pix's right
        /// and up, ending in a soft point; white tip ≈ 1/3 of its length.
        /// </summary>
        private static FighterGroupSpec Tail()
        {
            Vector3[] points =
            {
                new Vector3(0.05f, 0.29f, -0.22f), new Vector3(0.18f, 0.32f, -0.40f), new Vector3(0.34f, 0.43f, -0.47f),
                new Vector3(0.47f, 0.61f, -0.44f), new Vector3(0.53f, 0.81f, -0.36f), new Vector3(0.52f, 1.01f, -0.26f)
            };
            float[] radii = { 0.07f, 0.145f, 0.19f, 0.18f, 0.135f, 0.035f };
            var parts = new System.Collections.Generic.List<P>();
            for (int i = 0; i + 1 < points.Length; i++)
                parts.Add(P.Cone(points[i], points[i + 1], radii[i], radii[i + 1], i >= 3 ? Cream : Fur));
            // Plump middle (the brush of the tail), blended into the tube.
            parts.Add(P.Ellipsoid(new Vector3(0.40f, 0.52f, -0.46f), new Vector3(0.20f, 0.21f, 0.18f), Fur));
            parts.Add(P.Sphere(new Vector3(0.53f, 0.90f, -0.31f), 0.215f, Cream).AsPaint());
            return FighterGroupSpec.Sculpt("tail", FighterBone.Tail, 0.10f, 300, parts.ToArray());
        }

        /// <summary>Orange upper arm, dark forearm and paw; both paws on the blaster (right on the grip, left under the barrel).</summary>
        private static P[] Arm(float side)
        {
            var shoulder = new Vector3(0.21f * side, 0.70f, 0.02f);
            Vector3 elbow = side > 0f ? new Vector3(0.25f, 0.55f, 0.13f) : new Vector3(-0.25f, 0.57f, 0.20f);
            Vector3 paw = side > 0f ? new Vector3(0.09f, 0.48f, 0.25f) : new Vector3(-0.08f, 0.51f, 0.42f);
            return new[]
            {
                P.Cone(shoulder, elbow, 0.078f, 0.07f, Fur),
                P.Cone(elbow, paw, 0.07f, 0.072f, Dark),
                P.Ellipsoid(paw, new Vector3(0.085f, 0.075f, 0.085f), Dark)
            };
        }

        /// <summary>Short dark leg and an oval foot.</summary>
        private static P[] Leg(float side)
        {
            return new[]
            {
                P.Cone(new Vector3(0.13f * side, 0.20f, 0.03f), new Vector3(0.135f * side, 0.07f, 0.04f), 0.085f, 0.075f, Dark),
                P.Ellipsoid(new Vector3(0.135f * side, 0.05f, 0.075f), new Vector3(0.095f, 0.055f, 0.13f), Dark).WithSegments(10)
            };
        }

        /// <summary>Turquoise water blaster: body, grip, light glossy tank on top, short nozzle with a warm tip.</summary>
        private static P[] BlasterParts()
        {
            return new[]
            {
                P.Box(new Vector3(0f, 0.56f, 0.37f), new Vector3(0.075f, 0.062f, 0.18f), 0.045f, Blaster).Glossy(0.5f).WithSegments(5),
                P.Box(new Vector3(0.0f, 0.47f, 0.26f), new Vector3(0.045f, 0.075f, 0.05f), 0.03f, BlasterDark).Rotated(new Vector3(15f, 0f, 0f))
                    .WithSegments(5),
                P.Cone(new Vector3(0f, 0.665f, 0.25f), new Vector3(0f, 0.665f, 0.46f), 0.075f, 0.075f, Tank).Glossy(1f).WithSegments(10),
                P.Cone(new Vector3(0f, 0.57f, 0.53f), new Vector3(0f, 0.57f, 0.60f), 0.042f, 0.036f, BlasterDark).WithSegments(8),
                P.Cone(new Vector3(0f, 0.57f, 0.60f), new Vector3(0f, 0.57f, 0.625f), 0.04f, 0.032f, Nozzle).WithSegments(8)
            };
        }

        /// <summary>Big dark-brown eye with a thin white rim and one highlight, set high on the face and turned up and out.</summary>
        private static P[] Eye(float side)
        {
            Vector3 c = side > 0f ? EyeCenterR : Mirror(EyeCenterR);
            Quaternion rot = EyeRotation(side);
            return new[]
            {
                P.Ellipsoid(c, new Vector3(0.075f, 0.088f, 0.045f), EyeWhite).Oriented(rot).Glossy(0.6f),
                P.Ellipsoid(c + rot * new Vector3(0f, -0.004f, 0.02f), new Vector3(0.063f, 0.076f, 0.033f), Pupil).Oriented(rot).Glossy(1f),
                P.Ellipsoid(c + rot * new Vector3(0.022f * side, 0.03f, 0.05f), new Vector3(0.022f, 0.022f, 0.01f), EyeWhite).Oriented(rot)
                    .Glossy(1f).WithSegments(6)
            };
        }

        /// <summary>Fur-coloured lid over the whole eye (bone scale 0 = open).</summary>
        private static P[] Lid(float side)
        {
            Vector3 c = side > 0f ? EyeCenterR : Mirror(EyeCenterR);
            return new[] { P.Ellipsoid(c + EyeRotation(side) * new Vector3(0f, 0f, 0.008f), new Vector3(0.084f, 0.097f, 0.056f), Fur).Oriented(EyeRotation(side)) };
        }

        private static Quaternion EyeRotation(float side) => Quaternion.Euler(EyePitch, EyeYaw * side, 0f);

        private static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);
    }
}
