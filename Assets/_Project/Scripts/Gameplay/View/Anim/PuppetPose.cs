using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// Pose layers of the procedural "puppet" animation (brief A-TheoAnim §3). Pure functions of time / phase: no Transform, no
    /// Time, no allocations. Each layer adds into a <see cref="PoseBuffer"/> with a weight; t01 = 0 and t01 = 1 of a one-shot clip
    /// give no offset, so clips start and end on the base pose.
    /// </summary>
    public static class PuppetPose
    {
        private const float TwoPi = 2f * Mathf.PI;
        /// <summary>Squash/stretch keeps the volume roughly: the other two axes change by half as much, opposite way.</summary>
        private const float VolumeShare = 0.5f;
        /// <summary>The arm holding the prop swings with half the amplitude (brief §3).</summary>
        private const float PropArmShare = 0.5f;

        /// <summary>
        /// Step phase (cycles, 0–1) after the root moved <paramref name="distance"/> along its path. One cycle (left + right step)
        /// covers <paramref name="strideLength"/>, so the feet do not slide; a jump longer than <paramref name="teleportDistance"/>
        /// (teleport, correction) does not move the phase.
        /// </summary>
        public static float AdvanceRunPhase(float phase, float distance, float strideLength, float teleportDistance)
        {
            if (distance <= 0f || distance > teleportDistance || strideLength <= 0f) return phase;
            phase += distance / strideLength;
            return phase - Mathf.Floor(phase);
        }

        /// <summary>Breathing (body scale) and a slight head sway; periodic with 1 / BreathHz.</summary>
        public static void Idle(PoseBuffer p, FighterAnimConfig c, float t, float weight)
        {
            float s = Mathf.Sin(TwoPi * c.BreathHz * t);
            float b = c.BreathScale * s;
            p.AddScale(FighterBone.Body, new Vector3(-VolumeShare * b, b, -VolumeShare * b), weight);
            p.AddEuler(FighterBone.Head, new Vector3(0f, 0f, c.IdleSwayDeg * s), weight);
        }

        /// <summary>
        /// Run cycle at <paramref name="phase"/> (cycles): legs in antiphase, arms against the legs (prop arm at half), a hop per
        /// step, forward lean by <paramref name="speed01"/> and a lean into the turn (<paramref name="lean"/> −1…1, + = right).
        /// </summary>
        public static void Run(PoseBuffer p, FighterAnimConfig c, float phase, float speed01, float lean, float weight,
            bool twoHanded = false)
        {
            float s = Mathf.Sin(TwoPi * phase);
            float leg = c.LegSwingDeg * speed01 * s;
            float arm = c.ArmSwingDeg * speed01 * s;
            p.AddEuler(FighterBone.LegL, new Vector3(leg, 0f, 0f), weight);
            p.AddEuler(FighterBone.LegR, new Vector3(-leg, 0f, 0f), weight);
            float bounce = Mathf.Abs(s);
            if (twoHanded)
            {
                // Both hands on the prop: the arms bob together with the steps (the same pitch about the body's X axis keeps the grip).
                float bob = c.ArmSwingDeg * c.TwoHandSwingShare * speed01 * (bounce - VolumeShare);
                p.AddEuler(FighterBone.ArmL, new Vector3(bob, 0f, 0f), weight);
                p.AddEuler(FighterBone.ArmR, new Vector3(bob, 0f, 0f), weight);
            }
            else
            {
                p.AddEuler(FighterBone.ArmL, new Vector3(-arm, 0f, 0f), weight);
                p.AddEuler(FighterBone.ArmR, new Vector3(PropArmShare * arm, 0f, 0f), weight);
            }
            p.AddPosition(FighterBone.Root, new Vector3(0f, c.BounceHeight * speed01 * bounce, 0f), weight);
            p.AddEuler(FighterBone.Body, new Vector3(c.LeanForwardDeg * speed01, 0f, -c.LeanTurnDeg * lean), weight);
        }

        /// <summary>Head (and eyes) turn toward <paramref name="amount"/> (−1…1, + = right).</summary>
        public static void Look(PoseBuffer p, FighterAnimConfig c, float amount, float weight)
        {
            p.AddEuler(FighterBone.Head, new Vector3(0f, c.HeadLookDeg * amount, 0f), weight);
            p.AddEuler(FighterBone.EyeL, new Vector3(0f, c.LookMaxDeg * amount, 0f), weight);
            p.AddEuler(FighterBone.EyeR, new Vector3(0f, c.LookMaxDeg * amount, 0f), weight);
        }

        /// <summary>
        /// Shot recoil: arm and prop kick back (both arms together when the prop is held with both hands), the body rocks. Only the Shoot style has its
        /// own motion so far; the other styles use it until A.2b.
        /// </summary>
        public static void Shot(PoseBuffer p, FighterAnimConfig c, float t01, FighterAttackStyle style, float scale, float weight,
            bool twoHanded = false)
        {
            float e = ShotEnvelope(c, t01) * scale;
            if (twoHanded)
            {
                // The muzzle kicks up with both arms (the prop rides on the right arm).
                float kick = -c.RecoilDeg * c.TwoHandRecoilShare * e;
                p.AddEuler(FighterBone.ArmR, new Vector3(kick, 0f, 0f), weight);
                p.AddEuler(FighterBone.ArmL, new Vector3(kick, 0f, 0f), weight);
            }
            else
            {
                p.AddEuler(FighterBone.ArmR, new Vector3(-c.RecoilDeg * e, 0f, 0f), weight);
                p.AddEuler(FighterBone.Prop, new Vector3(-c.RecoilDeg * e, 0f, 0f), weight);
                // One hand: the prop also slides back along its axis (with both hands that would pull it out of the left hand).
                p.AddPosition(FighterBone.Prop, new Vector3(0f, 0f, -c.RecoilDistance * e), weight);
            }
            p.AddEuler(FighterBone.Body, new Vector3(-c.BodyRecoilDeg * e, 0f, 0f), weight);
        }

        /// <summary>Weight of the shot recoil at <paramref name="t01"/> (the config curve; a sine bump if the curve is empty).</summary>
        public static float ShotEnvelope(FighterAnimConfig c, float t01)
        {
            t01 = Mathf.Clamp01(t01);
            AnimationCurve curve = c.ShotEnvelope;
            if (curve == null || curve.length == 0) return Mathf.Sin(Mathf.PI * t01);
            return curve.Evaluate(t01);
        }

        /// <summary>Ult: crouch, then a jump with a body spin and a strong shot.</summary>
        public static void Ult(PoseBuffer p, FighterAnimConfig c, float t01, FighterAttackStyle style, float weight,
            bool twoHanded = false)
        {
            t01 = Mathf.Clamp01(t01);
            float f = c.UltCrouchFraction;
            if (t01 < f)
            {
                float k = t01 / f;
                p.AddPosition(FighterBone.Root, new Vector3(0f, -c.UltCrouch * Mathf.Sin(Mathf.PI * k), 0f), weight);
                return;
            }

            float u = (t01 - f) / (1f - f);
            p.AddPosition(FighterBone.Root, new Vector3(0f, c.UltJump * Mathf.Sin(Mathf.PI * u), 0f), weight);
            p.AddEuler(FighterBone.Body, new Vector3(0f, c.UltSpinDeg * Mathf.SmoothStep(0f, 1f, u), 0f), weight);
            Shot(p, c, u, style, c.UltShotScale, weight, twoHanded);
        }

        /// <summary>Trick: Roll = a forward somersault of the body; the other styles are A.2b placeholders (no motion).</summary>
        public static void Trick(PoseBuffer p, FighterAnimConfig c, float t01, FighterTrickStyle style, float weight)
        {
            if (style != FighterTrickStyle.Roll) return;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t01));
            p.AddEuler(FighterBone.Body, new Vector3(c.TrickRollDeg * k, 0f, 0f), weight);
        }

        /// <summary>
        /// Hit reaction: body squash and a tilt away from the hit (<paramref name="localFrom"/> — direction to the source in the
        /// body frame, x right, z forward), eyes closed while it lasts.
        /// </summary>
        public static void Hurt(PoseBuffer p, FighterAnimConfig c, float t01, Vector3 localFrom, float strength, float weight)
        {
            t01 = Mathf.Clamp01(t01);
            float e = Mathf.Sin(Mathf.PI * t01) * strength;
            float sq = c.HurtSquash * e;
            p.AddScale(FighterBone.Body, new Vector3(VolumeShare * sq, -sq, VolumeShare * sq), weight);
            float tilt = c.HurtTiltDeg * e;
            p.AddEuler(FighterBone.Body, new Vector3(-tilt * localFrom.z, 0f, tilt * localFrom.x), weight);
            if (t01 < 1f) CloseLids(p, weight);
        }

        /// <summary>Stunned: the body and head wobble against each other (periodic with 1 / StunWobbleHz), eyes half closed.</summary>
        public static void Stunned(PoseBuffer p, FighterAnimConfig c, float t, float weight)
        {
            float a = TwoPi * c.StunWobbleHz * t;
            float s = Mathf.Sin(a) * c.StunWobbleDeg;
            float k = Mathf.Cos(a) * c.StunWobbleDeg;
            p.AddEuler(FighterBone.Body, new Vector3(VolumeShare * k, 0f, s), weight);
            p.AddEuler(FighterBone.Head, new Vector3(0f, k, -s), weight);
            CloseLids(p, c.StunLidClosure * weight);
        }

        /// <summary>Spawn: the whole model grows from 0 (ease-out).</summary>
        public static void Spawn(PoseBuffer p, FighterAnimConfig c, float t01, float weight)
        {
            float r = 1f - Mathf.Clamp01(t01);
            float k = 1f - r * r;
            p.AddScale(FighterBone.Root, Vector3.one * (k - 1f), weight);
        }

        /// <summary>Defeat: backward tumble, the body and the whole model shrink to 0 at t01 = 1, eyes closed.</summary>
        public static void Defeat(PoseBuffer p, FighterAnimConfig c, float t01)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t01));
            p.AddEuler(FighterBone.Body, new Vector3(-c.DefeatTumbleDeg * k, 0f, 0f), 1f);
            p.AddScale(FighterBone.Body, Vector3.one * -k, 1f);
            p.AddScale(FighterBone.Root, Vector3.one * -k, 1f);
            CloseLids(p, 1f);
        }

        /// <summary>Victory: hops (periodic with 1 / VictoryHopHz) with both arms up.</summary>
        public static void Victory(PoseBuffer p, FighterAnimConfig c, float t, float weight, bool twoHanded = false)
        {
            float hop = Mathf.Abs(Mathf.Sin(Mathf.PI * c.VictoryHopHz * t));
            p.AddPosition(FighterBone.Root, new Vector3(0f, c.VictoryHopHeight * hop, 0f), weight);
            if (twoHanded)
            {
                // Lifts the prop with both hands, a little higher on each hop.
                float lift = -c.VictoryTwoHandDeg * (1f - VolumeShare + VolumeShare * hop);
                p.AddEuler(FighterBone.ArmL, new Vector3(lift, 0f, 0f), weight);
                p.AddEuler(FighterBone.ArmR, new Vector3(lift, 0f, 0f), weight);
                return;
            }
            p.AddEuler(FighterBone.ArmL, new Vector3(0f, 0f, -c.VictoryArmDeg), weight);
            p.AddEuler(FighterBone.ArmR, new Vector3(0f, 0f, c.VictoryArmDeg), weight);
        }

        /// <summary>Eyelid closure of a blink at <paramref name="t01"/>: 0 → 1 (closed at the middle) → 0.</summary>
        public static float BlinkClosure(float t01)
        {
            if (t01 <= 0f || t01 >= 1f) return 0f;
            return 1f - Mathf.Abs(2f * t01 - 1f);
        }

        public static void Blink(PoseBuffer p, float t01)
        {
            float closure = BlinkClosure(t01);
            p.MaxUniformScale(FighterBone.LidL, closure);
            p.MaxUniformScale(FighterBone.LidR, closure);
        }

        public static void CloseLids(PoseBuffer p, float closure)
        {
            p.MaxUniformScale(FighterBone.LidL, closure);
            p.MaxUniformScale(FighterBone.LidR, closure);
        }
    }
}
