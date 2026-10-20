using System.Collections.Generic;
using Arena.Core.Defs;
using Arena.Data;
using Arena.Editor.Data;
using Arena.Gameplay.View;
using Arena.Gameplay.View.Anim;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay.Anim
{
    public sealed class FighterAnimatorTests
    {
        private const float Dt = 1f / 60f;
        private const float TopHeight = 2f;
        private const float Tolerance = 1e-4f;

        private FighterAnimConfig _config;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<FighterAnimConfig>();
            DataSeeder.FillFighterAnim(_config);
            _created.Add(_config);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void OnShoot_AttackStartsShot_UltStartsUlt_TrickNeedsStyle()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.OnShoot(AbilitySlot.Attack, Vector3.forward);
            Assert.AreEqual(_config.ShotSeconds, a.State.ShotLeft);
            Assert.AreEqual(0f, a.State.UltLeft);

            a.OnShoot(AbilitySlot.Ult, Vector3.forward);
            Assert.AreEqual(_config.UltSeconds, a.State.UltLeft);

            a.OnShoot(AbilitySlot.Trick, Vector3.forward);
            Assert.AreEqual(0f, a.State.TrickLeft, "no trick clip without a style");

            FighterAnimator roll = MakeAnimator(FighterTrickStyle.Roll);
            roll.OnShoot(AbilitySlot.Trick, Vector3.forward);
            Assert.AreEqual(_config.TrickSeconds, roll.State.TrickLeft);
        }

        [Test]
        public void Shot_MovesPropDuringClip_EndsOnBasePose()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.OnShoot(AbilitySlot.Attack, Vector3.forward);
            a.Evaluate(Dt * 2f);
            Assert.Less(a.Pose[FighterBone.Prop].Position.z, 0f, "prop pushed back");

            for (int i = 0; i < 30; i++) a.Evaluate(Dt);
            Assert.AreEqual(0f, a.Pose[FighterBone.Prop].Position.magnitude, Tolerance);
        }

        [Test]
        public void OnHurt_ClosesLids()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.OnHurt(Vector3.left, false);
            a.Evaluate(Dt);
            Assert.AreEqual(1f, a.Pose[FighterBone.LidL].Scale.x, Tolerance);
            Assert.AreEqual(1f, a.Pose[FighterBone.LidR].Scale.x, Tolerance);
            Assert.Less(a.Pose[FighterBone.Body].Euler.z, 0f, "hit from the left tilts to the right");
        }

        [Test]
        public void Blinks_AreSpacedWithinInterval_Over1000Seconds()
        {
            _config.DoubleBlinkChance = 0f;
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            int lastCount = a.State.BlinkCount;
            float time = 0f;
            float lastBlink = -1f;
            int blinks = 0;
            bool closedSeen = false;
            for (int frame = 0; frame < 60000; frame++)
            {
                a.Evaluate(Dt);
                time += Dt;
                if (a.Pose[FighterBone.LidL].Scale.x > 0.5f) closedSeen = true;
                if (a.State.BlinkCount == lastCount) continue;
                lastCount = a.State.BlinkCount;
                if (lastBlink >= 0f)
                {
                    float interval = time - lastBlink;
                    Assert.GreaterOrEqual(interval, _config.BlinkIntervalMin - Dt, "blink " + blinks);
                    Assert.LessOrEqual(interval, _config.BlinkIntervalMax + Dt, "blink " + blinks);
                }
                lastBlink = time;
                blinks++;
            }
            Assert.Greater((float)blinks, 1000f / _config.BlinkIntervalMax - 1f);
            Assert.IsTrue(closedSeen, "the lids close during a blink");
        }

        [Test]
        public void Blinks_SameSlot_AreDeterministic_OtherSlotDiffers()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None, 2);
            FighterAnimator b = MakeAnimator(FighterTrickStyle.None, 2);
            FighterAnimator c = MakeAnimator(FighterTrickStyle.None, 3);
            Assert.AreEqual(a.State.NextBlinkIn, b.State.NextBlinkIn);
            Assert.AreNotEqual(a.State.NextBlinkIn, c.State.NextBlinkIn);
        }

        [Test]
        public void OnDeath_PlaysDefeat_ThenFinishes()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.OnDeath();
            a.Evaluate(Dt);
            Assert.IsFalse(a.DefeatFinished);
            Assert.Less(a.Pose[FighterBone.Body].Scale.y, 0f, "shrinking");

            a.OnShoot(AbilitySlot.Attack, Vector3.forward);
            Assert.AreEqual(0f, a.State.ShotLeft, "the dead do not shoot");

            for (int i = 0; i < Mathf.CeilToInt(_config.DefeatSeconds / Dt) + 1; i++) a.Evaluate(Dt);
            Assert.IsTrue(a.DefeatFinished);
            Vector3 body = BonePose.ComposeScale(Vector3.one, a.Pose[FighterBone.Body].Scale);
            Assert.AreEqual(0f, body.magnitude, Tolerance);

            a.OnSpawn();
            Assert.IsFalse(a.State.Dead);
            Assert.IsFalse(a.DefeatFinished);
        }

        [Test]
        public void Run_StartsAboveThreshold_PhaseFollowsPath()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            float speed = _config.RunSpeedRef;
            for (int i = 0; i < 60; i++)
            {
                a.OnMove(speed, Vector3.forward, Dt);
                a.Evaluate(Dt);
            }
            Assert.AreEqual(1f, a.State.RunWeight, Tolerance);
            Assert.AreNotEqual(0f, a.Pose[FighterBone.LegL].Euler.x + a.Pose[FighterBone.Root].Position.y);

            float phase = a.State.Phase;
            a.OnMove(speed, Vector3.forward, Dt);
            float stride = _config.StrideLengthPerHeight * TopHeight;
            Assert.AreEqual(Mathf.Repeat(phase + speed * Dt / stride, 1f), a.State.Phase, Tolerance);

            phase = a.State.Phase;
            a.OnMove((_config.TeleportDistance + 0.5f) / Dt, Vector3.forward, Dt);
            Assert.AreEqual(phase, a.State.Phase, "teleport does not move the phase");

            for (int i = 0; i < 120; i++)
            {
                a.OnMove(0f, Vector3.zero, Dt);
                a.Evaluate(Dt);
            }
            Assert.AreEqual(0f, a.State.RunWeight, Tolerance);
        }

        [Test]
        public void Springs_SwingOnAcceleration_StayClamped()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            float maxSeen = 0f;
            for (int i = 0; i < 30; i++)
            {
                a.OnMove(_config.RunSpeedRef, Vector3.forward, Dt);
                a.Evaluate(Dt);
                maxSeen = Mathf.Max(maxSeen, Mathf.Abs(a.State.Springs[0].Angle));
            }
            Assert.Greater(maxSeen, 0f, "the head lags behind the start of the run");
            Assert.LessOrEqual(maxSeen, _config.SpringMaxDegrees + Tolerance);
        }

        [Test]
        public void ResetAll_ReturnsBonesToRest()
        {
            FighterRig rig = Rig();
            FighterAnimator a = MakeAnimator(FighterTrickStyle.Roll);
            a.Bind(rig);
            a.OnShoot(AbilitySlot.Ult, Vector3.forward);
            a.OnHurt(Vector3.forward, true);
            for (int i = 0; i < 10; i++)
            {
                a.OnMove(_config.RunSpeedRef, Vector3.right, Dt);
                a.Evaluate(Dt);
            }
            a.Apply(rig);
            Assert.AreNotEqual(rig.RestRotations[(int)FighterBone.LegL], rig.Bones[(int)FighterBone.LegL].localRotation);

            a.ResetAll();
            Assert.AreEqual(0f, a.State.RunWeight);
            Assert.AreEqual(0f, a.State.Springs[0].Angle);
            a.Apply(rig);
            for (int i = 0; i < rig.Bones.Length; i++)
            {
                Transform t = rig.Bones[i];
                if (t == null) continue;
                Assert.AreEqual(0f, (t.localPosition - rig.RestPositions[i]).magnitude, Tolerance, "position of bone " + i);
                Assert.AreEqual(1f, Mathf.Abs(Quaternion.Dot(t.localRotation, rig.RestRotations[i])), Tolerance, "rotation of bone " + i);
                Assert.AreEqual(0f, (t.localScale - rig.RestScales[i]).magnitude, Tolerance, "scale of bone " + i);
            }
        }

        [Test]
        public void Bind_TakesRigData()
        {
            FighterRig rig = Rig();
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.Bind(rig);
            Assert.AreEqual(rig.TopHeight, a.TopHeight);
            Assert.AreEqual(1, a.SpringCount);
            a.OnShoot(AbilitySlot.Trick, Vector3.forward);
            Assert.AreEqual(_config.TrickSeconds, a.State.TrickLeft, "rig trick style Roll");
        }

        [Test]
        public void EvaluateAndApply_DoNotAllocate()
        {
            FighterRig rig = Rig();
            FighterAnimator a = MakeAnimator(FighterTrickStyle.Roll);
            a.Bind(rig);
            Frame(a, rig); // warm-up (class initialization, D-058)
            a.OnShoot(AbilitySlot.Attack, Vector3.forward);
            a.OnShoot(AbilitySlot.Ult, Vector3.forward);
            a.OnShoot(AbilitySlot.Trick, Vector3.forward);
            a.OnHurt(Vector3.left, true);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 300; i++) Frame(a, rig);
            });
        }

        [Test]
        public void Stunned_WobblesAndHalfClosesLids_BlendsOut()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.SetStatus(true, false);
            for (int i = 0; i < 20; i++) a.Evaluate(Dt);
            Assert.AreEqual(1f, a.State.StunWeight, Tolerance);
            Assert.AreEqual(_config.StunLidClosure, a.Pose[FighterBone.LidL].Scale.x, 0.01f);
            a.SetStatus(false, false);
            for (int i = 0; i < 20; i++) a.Evaluate(Dt);
            Assert.AreEqual(0f, a.State.StunWeight, Tolerance);
        }

        [Test]
        public void Trick_KeepsRollingWhileDashing()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.Roll);
            a.OnShoot(AbilitySlot.Trick, Vector3.forward);
            a.SetStatus(false, true);
            int frames = Mathf.CeilToInt(_config.TrickSeconds * 2f / Dt);
            for (int i = 0; i < frames; i++) a.Evaluate(Dt);
            Assert.Greater(a.State.TrickLeft, 0f, "still rolling during a long dash");
            a.SetStatus(false, false);
            for (int i = 0; i < frames; i++) a.Evaluate(Dt);
            Assert.LessOrEqual(a.State.TrickLeft, 0f, "ends after the dash");
        }

        [Test]
        public void Muzzle_RequestedOncePerShot_UltAfterCrouch()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.OnShoot(AbilitySlot.Attack, Vector3.forward);
            Assert.IsTrue(a.ConsumeMuzzle(out float scale));
            Assert.AreEqual(1f, scale);
            Assert.IsFalse(a.ConsumeMuzzle(out _), "consumed");

            a.OnShoot(AbilitySlot.Ult, Vector3.forward);
            a.Evaluate(Dt);
            Assert.IsFalse(a.ConsumeMuzzle(out _), "crouching: no shot yet");
            for (int i = 0; i < Mathf.CeilToInt(_config.UltSeconds / Dt); i++) a.Evaluate(Dt);
            Assert.IsTrue(a.ConsumeMuzzle(out scale));
            Assert.AreEqual(_config.UltShotScale, scale);
        }

        [Test]
        public void TriggerBlink_ClosesLidsMidway()
        {
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.TriggerBlink();
            float maxClosure = 0f;
            for (int i = 0; i < 10; i++)
            {
                a.Evaluate(Dt);
                maxClosure = Mathf.Max(maxClosure, a.Pose[FighterBone.LidL].Scale.x);
            }
            Assert.Greater(maxClosure, 0.5f);
        }

        [Test]
        public void TwoHandedProp_GripHoldsThroughRunShotUltVictory()
        {
            FighterRig rig = TwoHandedRig(out Transform hand, out Transform grip);
            FighterAnimator a = MakeAnimator(FighterTrickStyle.None);
            a.Bind(rig);
            Assert.IsTrue(a.TwoHanded);
            Assert.AreEqual(0f, Vector3.Distance(hand.position, grip.position), 1e-4f, "grip at rest");

            float worst = 0f;
            int worstFrame = -1;
            float armTurn = 0f;
            Quaternion armRest = rig.Bones[(int)FighterBone.ArmR].localRotation;
            for (int i = 0; i < 120; i++)
            {
                a.OnMove(_config.RunSpeedRef, Vector3.forward, Dt);
                if (i == 40) a.OnShoot(AbilitySlot.Attack, Vector3.forward);
                if (i == 60) a.OnShoot(AbilitySlot.Ult, Vector3.forward);
                if (i == 100) a.OnMatchEnd(true);
                a.Evaluate(Dt);
                a.Apply(rig);
                float d = Vector3.Distance(hand.position, grip.position);
                if (d > worst) { worst = d; worstFrame = i; }
                armTurn = Mathf.Max(armTurn, Quaternion.Angle(armRest, rig.Bones[(int)FighterBone.ArmR].localRotation));
            }
            Assert.Greater(armTurn, 5f, "the arms do move");
            Assert.Less(worst, 0.01f, "the left hand stays on the prop (worst " + worst + " at frame " + worstFrame + ")");
        }

        [Test]
        public void OneHanded_RigWithPropOnBody_IsNotTwoHanded()
        {
            FighterRig rig = Rig();
            Assert.IsFalse(FighterAnimator.HoldsPropWithBothHands(rig));
        }

        /// <summary>Body with two shoulders (different rest turns), the prop on the right arm; "hand" on the left arm sits on "grip" of the prop.</summary>
        private FighterRig TwoHandedRig(out Transform hand, out Transform grip)
        {
            FighterRig rig = Rig();
            Transform body = rig.Bones[(int)FighterBone.Body];
            Transform armL = rig.Bones[(int)FighterBone.ArmL];
            Transform armR = rig.Bones[(int)FighterBone.ArmR];
            Transform prop = rig.Bones[(int)FighterBone.Prop];
            armL.SetParent(body, false);
            armR.SetParent(body, false);
            prop.SetParent(armR, false);
            Set(rig, FighterBone.Body, new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            Set(rig, FighterBone.ArmL, new Vector3(-0.135f, 0.62f, 0f), Quaternion.Euler(-67.3f, 18.2f, 34.9f));
            Set(rig, FighterBone.ArmR, new Vector3(0.135f, 0.62f, 0f), Quaternion.Euler(-44.5f, -17.8f, -20f));
            Set(rig, FighterBone.Prop, new Vector3(0.28f, 0.351f, 0.049f), Quaternion.Euler(30f, 10f, 0f));
            rig.ResetPose();
            grip = new GameObject("Grip").transform;
            grip.SetParent(prop, false);
            grip.localPosition = new Vector3(0f, 0.05f, 0.3f);
            hand = new GameObject("Hand").transform;
            hand.SetParent(armL, true);
            hand.position = grip.position;
            return rig;
        }

        private static void Set(FighterRig rig, FighterBone bone, Vector3 position, Quaternion rotation)
        {
            rig.RestPositions[(int)bone] = position;
            rig.RestRotations[(int)bone] = rotation;
        }

        private void Frame(FighterAnimator a, FighterRig rig)
        {
            a.OnMove(_config.RunSpeedRef, Vector3.forward, Dt);
            a.SetLook(Vector3.forward);
            a.SetStatus(false, false);
            a.Evaluate(Dt);
            a.Apply(rig);
        }

        private FighterAnimator MakeAnimator(FighterTrickStyle trick, int slot = 0)
        {
            var a = new FighterAnimator(_config, slot);
            var springs = new[] { new FighterSpring { Bone = FighterBone.Head, Frequency = 3.5f, Damping = 0.55f, Amount = 6f } };
            a.Configure(TopHeight, FighterAttackStyle.Shoot, FighterAttackStyle.Shoot, trick, springs);
            return a;
        }

        /// <summary>A rig built from code: every bone except Tail and Extra, lids at scale 0 (open).</summary>
        private FighterRig Rig()
        {
            var root = new GameObject("RigRoot");
            _created.Add(root);
            FighterRig rig = root.AddComponent<FighterRig>();
            int n = PoseBuffer.BoneCount;
            rig.Bones = new Transform[n];
            rig.RestPositions = new Vector3[n];
            rig.RestRotations = new Quaternion[n];
            rig.RestScales = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var bone = (FighterBone)i;
                rig.RestPositions[i] = new Vector3(0.1f * i, 0.2f * i, 0f);
                rig.RestRotations[i] = Quaternion.Euler(0f, 5f * i, 0f);
                rig.RestScales[i] = bone == FighterBone.LidL || bone == FighterBone.LidR ? Vector3.zero : Vector3.one;
                if (bone == FighterBone.Tail || bone == FighterBone.Extra) continue;
                var go = new GameObject(bone.ToString());
                go.transform.SetParent(root.transform, false);
                rig.Bones[i] = go.transform;
            }
            rig.TopHeight = TopHeight;
            rig.AttackStyle = FighterAttackStyle.Shoot;
            rig.UltStyle = FighterAttackStyle.Shoot;
            rig.TrickStyle = FighterTrickStyle.Roll;
            rig.Springs = new[] { new FighterSpring { Bone = FighterBone.Head, Frequency = 3.5f, Damping = 0.55f, Amount = 6f } };
            rig.ResetPose();
            return rig;
        }
    }
}
