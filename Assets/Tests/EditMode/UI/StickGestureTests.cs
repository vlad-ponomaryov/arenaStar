using Arena.UI.Controls;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.EditMode.UI
{
    public sealed class StickGestureTests
    {
        private static readonly StickGestureSettings Settings = new StickGestureSettings(0.2f, 0.25f, 0.35f);

        [Test]
        public void ShortTap_WithoutDrag_IsAutoAim()
        {
            var g = new StickGesture();
            g.Begin();
            g.Move(new Vector2(0.05f, 0.05f));
            Assert.AreEqual(Vector2.zero, g.CurrentAim(Settings), "no aim line before the drag");

            Assert.AreEqual(StickGestureResult.Tap, g.End(Settings, out Vector2 aim));
            Assert.AreEqual(Vector2.zero, aim);
            Assert.IsFalse(g.IsActive);
        }

        [Test]
        public void LongHold_WithoutDrag_IsStillAutoAim()
        {
            var g = new StickGesture();
            g.Begin();
            Assert.IsTrue(g.HeldStill(Settings), "held still: the HUD previews the auto-aim target");
            Assert.AreEqual(StickGestureResult.Tap, g.End(Settings, out _), "no time limit on a tap (D-129)");
        }

        [Test]
        public void FingerJitter_WithinTapZone_IsAutoAim_NotCancelOrManual()
        {
            // The old classification (dead zone 0.2) made this a drag released in the cancel zone — no shot at all.
            var g = new StickGesture();
            g.Begin();
            g.Move(new Vector2(0.28f, 0.1f));
            g.Move(new Vector2(0.12f, 0.05f));
            Assert.IsFalse(g.Dragged(Settings));
            Assert.AreEqual(Vector2.zero, g.CurrentAim(Settings), "no aim line for a jitter");
            Assert.AreEqual(StickGestureResult.Tap, g.End(Settings, out Vector2 aim));
            Assert.AreEqual(Vector2.zero, aim);
        }

        [Test]
        public void DragBeyondTapZone_ThenRelease_IsManualWithDirection()
        {
            var g = new StickGesture();
            g.Begin();
            g.Move(new Vector2(0.3f, 0.4f));
            Assert.IsTrue(g.Dragged(Settings));
            Assert.IsFalse(g.HeldStill(Settings));
            Assert.AreEqual(new Vector2(0.3f, 0.4f), g.CurrentAim(Settings));

            Assert.AreEqual(StickGestureResult.Manual, g.End(Settings, out Vector2 aim), "a quick flick is still manual");
            Assert.AreEqual(0.3f, aim.x, 1e-5f);
            Assert.AreEqual(0.4f, aim.y, 1e-5f);
        }

        [Test]
        public void Drag_IsClampedToUnitLength()
        {
            var g = new StickGesture();
            g.Begin();
            g.Move(new Vector2(3f, 4f));
            g.End(Settings, out Vector2 aim);
            Assert.AreEqual(1f, aim.magnitude, 1e-5f);
            Assert.AreEqual(0.6f, aim.x, 1e-5f);
        }

        [Test]
        public void ReleaseInCancelZone_AfterDrag_IsCancel()
        {
            var g = new StickGesture();
            g.Begin();
            g.Move(new Vector2(0f, 0.8f));
            g.Move(new Vector2(0f, 0.1f));
            Assert.AreEqual(Vector2.zero, g.CurrentAim(Settings), "aim line hidden inside the cancel zone");

            Assert.AreEqual(StickGestureResult.Cancel, g.End(Settings, out Vector2 aim));
            Assert.AreEqual(Vector2.zero, aim);
        }

        [Test]
        public void EndWithoutBegin_AndAbort_GiveNothing()
        {
            var g = new StickGesture();
            Assert.AreEqual(StickGestureResult.None, g.End(Settings, out _));

            g.Begin();
            g.Move(new Vector2(0.9f, 0f));
            g.Abort();
            Assert.IsFalse(g.IsActive);
            Assert.AreEqual(StickGestureResult.None, g.End(Settings, out _));
        }
    }
}
