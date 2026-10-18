using Arena.UI.Controls;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.UI
{
    public sealed class StickGestureTests
    {
        private static readonly StickGestureSettings Settings = new StickGestureSettings(0.2f, 0.25f, 0.25f);

        [Test]
        public void ShortTap_WithoutDrag_IsAutoAim()
        {
            var g = new StickGesture();
            g.Begin(10f);
            g.Move(new Vector2(0.05f, 0.05f));
            Assert.AreEqual(Vector2.zero, g.CurrentAim(Settings), "no aim line before the drag");

            Assert.AreEqual(StickGestureResult.Tap, g.End(10.1f, Settings, out Vector2 aim));
            Assert.AreEqual(Vector2.zero, aim);
            Assert.IsFalse(g.IsActive);
        }

        [Test]
        public void LongHold_WithoutDrag_DoesNothing()
        {
            var g = new StickGesture();
            g.Begin(0f);
            Assert.AreEqual(StickGestureResult.None, g.End(1f, Settings, out _));
        }

        [Test]
        public void DragBeyondDeadZone_ThenRelease_IsManualWithDirection()
        {
            var g = new StickGesture();
            g.Begin(0f);
            g.Move(new Vector2(0.3f, 0.4f));
            Assert.IsTrue(g.Dragged(Settings));
            Assert.AreEqual(new Vector2(0.3f, 0.4f), g.CurrentAim(Settings));

            Assert.AreEqual(StickGestureResult.Manual, g.End(0.05f, Settings, out Vector2 aim), "a quick flick is still manual");
            Assert.AreEqual(0.3f, aim.x, 1e-5f);
            Assert.AreEqual(0.4f, aim.y, 1e-5f);
        }

        [Test]
        public void Drag_IsClampedToUnitLength()
        {
            var g = new StickGesture();
            g.Begin(0f);
            g.Move(new Vector2(3f, 4f));
            g.End(1f, Settings, out Vector2 aim);
            Assert.AreEqual(1f, aim.magnitude, 1e-5f);
            Assert.AreEqual(0.6f, aim.x, 1e-5f);
        }

        [Test]
        public void ReleaseInCancelZone_AfterDrag_IsCancel()
        {
            var g = new StickGesture();
            g.Begin(0f);
            g.Move(new Vector2(0f, 0.8f));
            g.Move(new Vector2(0f, 0.1f));
            Assert.AreEqual(Vector2.zero, g.CurrentAim(Settings), "aim line hidden inside the cancel zone");

            Assert.AreEqual(StickGestureResult.Cancel, g.End(0.1f, Settings, out Vector2 aim));
            Assert.AreEqual(Vector2.zero, aim);
        }

        [Test]
        public void EndWithoutBegin_AndAbort_GiveNothing()
        {
            var g = new StickGesture();
            Assert.AreEqual(StickGestureResult.None, g.End(0f, Settings, out _));

            g.Begin(0f);
            g.Move(new Vector2(0.9f, 0f));
            g.Abort();
            Assert.IsFalse(g.IsActive);
            Assert.AreEqual(StickGestureResult.None, g.End(0.1f, Settings, out _));
        }
    }
}
