using Arena.Data;
using Arena.Editor.Data;
using Arena.Gameplay.View;
using Arena.Gameplay.View.Anim;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay.Anim
{
    public sealed class PuppetPoseTests
    {
        private const float Tolerance = 1e-4f;
        private FighterAnimConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<FighterAnimConfig>();
            DataSeeder.FillFighterAnim(_config);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void SameInputs_GiveSamePose()
        {
            var a = new PoseBuffer();
            var b = new PoseBuffer();
            Fill(a);
            Fill(b);
            for (int i = 0; i < PoseBuffer.BoneCount; i++)
            {
                Assert.AreEqual(a.Bones[i].Position, b.Bones[i].Position);
                Assert.AreEqual(a.Bones[i].Euler, b.Bones[i].Euler);
                Assert.AreEqual(a.Bones[i].Scale, b.Bones[i].Scale);
            }
        }

        [Test]
        public void Idle_IsPeriodicWithBreath()
        {
            var a = new PoseBuffer();
            var b = new PoseBuffer();
            PuppetPose.Idle(a, _config, 0f, 1f);
            PuppetPose.Idle(b, _config, 1f / _config.BreathHz, 1f);
            AssertSame(a, b);

            var mid = new PoseBuffer();
            PuppetPose.Idle(mid, _config, 0.25f / _config.BreathHz, 1f);
            Assert.Greater(mid[FighterBone.Body].Scale.y, 0f, "breathing in at a quarter period");
        }

        [Test]
        public void RunPhase_GrowsExactlyByPathOverStride()
        {
            float stride = 1.1f;
            float phase = PuppetPose.AdvanceRunPhase(0.1f, 0.22f, stride, _config.TeleportDistance);
            Assert.AreEqual(0.1f + 0.22f / stride, phase, Tolerance);

            // A full stride brings the phase back (wraps to 0–1).
            float wrapped = PuppetPose.AdvanceRunPhase(0.3f, stride * 0.9f, stride, 2f * stride);
            Assert.AreEqual(0.2f, wrapped, Tolerance);
        }

        [Test]
        public void RunPhase_StandingOrTeleport_DoesNotMove()
        {
            Assert.AreEqual(0.4f, PuppetPose.AdvanceRunPhase(0.4f, 0f, 1f, _config.TeleportDistance));
            Assert.AreEqual(0.4f, PuppetPose.AdvanceRunPhase(0.4f, _config.TeleportDistance + 0.01f, 1f, _config.TeleportDistance));
        }

        [Test]
        public void Run_LegsInAntiphase()
        {
            var p = new PoseBuffer();
            PuppetPose.Run(p, _config, 0.25f, 1f, 0f, 1f);
            float left = p[FighterBone.LegL].Euler.x;
            Assert.AreEqual(_config.LegSwingDeg, left, Tolerance);
            Assert.AreEqual(-left, p[FighterBone.LegR].Euler.x, Tolerance);
            Assert.AreEqual(-Mathf.Sign(left), Mathf.Sign(p[FighterBone.ArmL].Euler.x), "left arm swings against the left leg");
            Assert.Greater(p[FighterBone.Root].Position.y, 0f, "hop");
        }

        [Test]
        public void Shot_PropOffsetIsZeroAtEnds_PeaksInside()
        {
            var start = new PoseBuffer();
            var end = new PoseBuffer();
            PuppetPose.Shot(start, _config, 0f, FighterAttackStyle.Shoot, 1f, 1f);
            PuppetPose.Shot(end, _config, 1f, FighterAttackStyle.Shoot, 1f, 1f);
            Assert.AreEqual(0f, start[FighterBone.Prop].Position.magnitude, Tolerance);
            Assert.AreEqual(0f, end[FighterBone.Prop].Position.magnitude, Tolerance);

            float peak = 0f;
            for (int i = 1; i < 20; i++)
            {
                var p = new PoseBuffer();
                PuppetPose.Shot(p, _config, i / 20f, FighterAttackStyle.Shoot, 1f, 1f);
                peak = Mathf.Max(peak, -p[FighterBone.Prop].Position.z);
            }
            Assert.Greater(peak, 0.5f * _config.RecoilDistance);
        }

        [Test]
        public void Defeat_AtEnd_BodyScaleIsZero_LidsClosed()
        {
            var p = new PoseBuffer();
            PuppetPose.Defeat(p, _config, 1f);
            Vector3 body = BonePose.ComposeScale(Vector3.one, p[FighterBone.Body].Scale);
            Assert.AreEqual(0f, body.magnitude, Tolerance);
            Assert.AreEqual(1f, p[FighterBone.LidL].Scale.x, Tolerance);
        }

        [Test]
        public void Blink_ClosesInTheMiddle()
        {
            Assert.AreEqual(0f, PuppetPose.BlinkClosure(0f));
            Assert.AreEqual(1f, PuppetPose.BlinkClosure(0.5f), Tolerance);
            Assert.AreEqual(0f, PuppetPose.BlinkClosure(1f));
        }

        [Test]
        public void Hurt_ClosesLids_ZeroAtEnds()
        {
            var p = new PoseBuffer();
            PuppetPose.Hurt(p, _config, 0.5f, Vector3.forward, 1f, 1f);
            Assert.AreEqual(1f, p[FighterBone.LidL].Scale.x, Tolerance);
            Assert.AreEqual(1f, p[FighterBone.LidR].Scale.x, Tolerance);
            Assert.Less(p[FighterBone.Body].Scale.y, 0f, "squash");
            Assert.Less(p[FighterBone.Body].Euler.x, 0f, "hit from the front tilts back");

            var end = new PoseBuffer();
            PuppetPose.Hurt(end, _config, 1f, Vector3.forward, 1f, 1f);
            Assert.AreEqual(0f, end[FighterBone.Body].Scale.magnitude, Tolerance);
            Assert.AreEqual(0f, end[FighterBone.LidL].Scale.x);
        }

        [Test]
        public void Stunned_IsPeriodic_ZeroAtStart()
        {
            var a = new PoseBuffer();
            var b = new PoseBuffer();
            PuppetPose.Stunned(a, _config, 0.1f, 1f);
            PuppetPose.Stunned(b, _config, 0.1f + 1f / _config.StunWobbleHz, 1f);
            AssertSame(a, b);
            var zero = new PoseBuffer();
            PuppetPose.Stunned(zero, _config, 0f, 1f);
            Assert.AreEqual(0f, zero[FighterBone.Body].Euler.z, Tolerance);
        }

        [Test]
        public void ComposeScale_ZeroRestTakesOffset()
        {
            Assert.AreEqual(new Vector3(2f, 1f, 0.5f), BonePose.ComposeScale(new Vector3(2f, 1f, 0.5f), Vector3.zero));
            Assert.AreEqual(new Vector3(1.1f, 2.2f, 0.7f), BonePose.ComposeScale(new Vector3(1f, 2f, 0f), new Vector3(0.1f, 0.1f, 0.7f)));
        }

        private void Fill(PoseBuffer p)
        {
            PuppetPose.Idle(p, _config, 0.37f, 0.4f);
            PuppetPose.Run(p, _config, 0.61f, 0.8f, 0.3f, 0.6f);
            PuppetPose.Shot(p, _config, 0.2f, FighterAttackStyle.Shoot, 1f, 1f);
            PuppetPose.Ult(p, _config, 0.7f, FighterAttackStyle.Shoot, 1f);
            PuppetPose.Trick(p, _config, 0.4f, FighterTrickStyle.Roll, 1f);
            PuppetPose.Hurt(p, _config, 0.3f, Vector3.right, 1.5f, 1f);
            PuppetPose.Blink(p, 0.3f);
        }

        private static void AssertSame(PoseBuffer a, PoseBuffer b)
        {
            for (int i = 0; i < PoseBuffer.BoneCount; i++)
            {
                Assert.AreEqual(0f, (a.Bones[i].Position - b.Bones[i].Position).magnitude, Tolerance, "position of bone " + i);
                Assert.AreEqual(0f, (a.Bones[i].Euler - b.Bones[i].Euler).magnitude, Tolerance, "rotation of bone " + i);
                Assert.AreEqual(0f, (a.Bones[i].Scale - b.Bones[i].Scale).magnitude, Tolerance, "scale of bone " + i);
            }
        }
    }
}
