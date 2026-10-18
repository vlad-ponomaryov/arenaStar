using Arena.Gameplay.Cameras;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class CameraMathTests
    {
        private const float Pitch = 55f;
        private const float Distance = 18f;
        private const float Offset = 2f;
        private const float Inset = 7f;
        private static readonly Vector2 Bounds = new Vector2(21f, 31f);

        [Test]
        public void Team0_LooksTowardsPlusZ_FromBelowTheFocus()
        {
            var target = new Vector3(4f, 0f, 15f);
            Vector3 focus = CameraMath.Focus(target, Bounds, false, Offset, Inset);
            Assert.AreEqual(10.5f, focus.x, 1e-5f, "centred across the map");
            Assert.AreEqual(17f, focus.z, 1e-5f, "shifted forward towards the enemy");
            Assert.AreEqual(0f, focus.y);

            Quaternion rot = CameraMath.Rotation(Pitch, false);
            Vector3 pos = CameraMath.Position(focus, rot, Distance);
            Assert.Less(pos.z, focus.z, "camera behind the focus (own base at the bottom)");
            Assert.Greater(pos.y, 0f);
            Assert.AreEqual(Distance, Vector3.Distance(pos, focus), 1e-4f);
            Assert.AreEqual(Distance * Mathf.Sin(Pitch * Mathf.Deg2Rad), pos.y, 1e-4f);
            Assert.AreEqual(10.5f, pos.x, 1e-4f);
            Vector3 forward = rot * Vector3.forward;
            Assert.Greater(forward.z, 0f);
        }

        [Test]
        public void Team1_IsRotated180_AroundTheFocus()
        {
            var target = new Vector3(4f, 0f, 15f);
            Vector3 focus = CameraMath.Focus(target, Bounds, true, Offset, Inset);
            Assert.AreEqual(13f, focus.z, 1e-5f, "forward for team 1 is −Z");

            Quaternion rot = CameraMath.Rotation(Pitch, true);
            Vector3 pos = CameraMath.Position(focus, rot, Distance);
            Assert.Greater(pos.z, focus.z, "camera on the far side, team 1 base at the bottom of the screen");
            Assert.AreEqual(10.5f, pos.x, 1e-4f);
            Assert.Less((rot * Vector3.forward).z, 0f);
            Assert.AreEqual(Distance * Mathf.Sin(Pitch * Mathf.Deg2Rad), pos.y, 1e-4f);
        }

        [Test]
        public void Focus_IsClampedByTheMapEdges()
        {
            Assert.AreEqual(Inset, CameraMath.Focus(new Vector3(10f, 0f, 0f), Bounds, false, Offset, Inset).z, 1e-5f);
            Assert.AreEqual(31f - Inset, CameraMath.Focus(new Vector3(10f, 0f, 31f), Bounds, false, Offset, Inset).z, 1e-5f);
            Assert.AreEqual(Inset, CameraMath.Focus(new Vector3(10f, 0f, 0f), Bounds, true, Offset, Inset).z, 1e-5f);
            Assert.AreEqual(31f - Inset, CameraMath.Focus(new Vector3(10f, 0f, 40f), Bounds, true, Offset, Inset).z, 1e-5f);
        }

        [Test]
        public void Focus_OnAMapShorterThanTwoInsets_IsTheCentre()
        {
            var small = new Vector2(10f, 10f);
            Assert.AreEqual(5f, CameraMath.Focus(new Vector3(1f, 0f, 1f), small, false, Offset, Inset).z, 1e-5f);
        }

        [Test]
        public void DampFactor_IsInZeroOne_AndOneWithoutSmoothing()
        {
            Assert.AreEqual(1f, CameraMath.DampFactor(0f, 0.016f));
            float k = CameraMath.DampFactor(8f, 0.016f);
            Assert.Greater(k, 0f);
            Assert.Less(k, 1f);
            Assert.Greater(CameraMath.DampFactor(8f, 0.1f), k, "longer frame — larger step");
        }

        [Test]
        public void FocusFree_FollowsBothAxes_ClampedBySidesAndEdges()
        {
            var map = new Vector2(45f, 45f);
            Vector3 mid = CameraMath.FocusFree(new Vector3(20f, 0f, 22f), map, 1f, Inset, 10f);
            Assert.AreEqual(20f, mid.x, 1e-5f, "follows along X");
            Assert.AreEqual(23f, mid.z, 1e-5f, "follows along Z with the forward offset");
            Vector3 corner = CameraMath.FocusFree(new Vector3(1f, 0f, 44f), map, 1f, Inset, 10f);
            Assert.AreEqual(10f, corner.x, 1e-5f, "side inset");
            Assert.AreEqual(45f - Inset, corner.z, 1e-5f, "far edge inset");
            Vector3 narrow = CameraMath.FocusFree(new Vector3(1f, 0f, 3f), new Vector2(15f, 45f), 1f, Inset, 10f);
            Assert.AreEqual(7.5f, narrow.x, 1e-5f, "an axis narrower than twice the inset stays centred");
        }
    }
}
