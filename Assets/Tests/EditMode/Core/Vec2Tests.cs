using Arena.Core.Math;
using Arena.Core.Sim;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class Vec2Tests
    {
        private const float Tol = 1e-5f;

        [Test]
        public void Arithmetic_And_Length()
        {
            var a = new Vec2(3f, 4f);
            Assert.AreEqual(5f, a.Length, Tol);
            Assert.AreEqual(25f, a.SqrLength, Tol);
            Assert.AreEqual(new Vec2(4f, 6f), a + new Vec2(1f, 2f));
            Assert.AreEqual(new Vec2(2f, 2f), a - new Vec2(1f, 2f));
            Assert.AreEqual(new Vec2(6f, 8f), a * 2f);
            Assert.AreEqual(new Vec2(6f, 8f), 2f * a);
            Assert.AreEqual(new Vec2(-3f, -4f), -a);
            Assert.AreEqual(11f, Vec2.Dot(a, new Vec2(1f, 2f)), Tol);
            Assert.AreEqual(2f, Vec2.Cross(a, new Vec2(1f, 2f)), Tol);
            Assert.AreEqual(5f, Vec2.Distance(Vec2.Zero, a), Tol);
        }

        [Test]
        public void Normalized_UnitLength_ZeroStaysZero()
        {
            Assert.AreEqual(1f, new Vec2(3f, -4f).Normalized.Length, Tol);
            Assert.AreEqual(Vec2.Zero, Vec2.Zero.Normalized);
            Assert.IsTrue(Vec2.Zero.IsZero);
            Assert.IsFalse(new Vec2(0.01f, 0f).IsZero);
        }

        [Test]
        public void ClampLength_ShortensOnlyLongVectors()
        {
            Assert.AreEqual(1f, Vec2.ClampLength(new Vec2(1f, 1f), 1f).Length, Tol);
            Assert.AreEqual(new Vec2(0.3f, 0.4f), Vec2.ClampLength(new Vec2(0.3f, 0.4f), 1f));
        }

        [Test]
        public void Lerp_ClampsT()
        {
            Assert.IsTrue(Vec2.Approximately(new Vec2(5f, 0f), Vec2.Lerp(Vec2.Zero, new Vec2(10f, 0f), 0.5f), Tol));
            Assert.AreEqual(new Vec2(10f, 0f), Vec2.Lerp(Vec2.Zero, new Vec2(10f, 0f), 2f));
        }

        [Test]
        public void Angles_RoundTrip_And_Rotate()
        {
            Assert.IsTrue(Vec2.Approximately(Vec2.Up, MathUtil.AngleToDir(90f), Tol));
            Assert.AreEqual(90f, MathUtil.DirToAngle(Vec2.Up), 1e-3f);
            Assert.AreEqual(-45f, MathUtil.DirToAngle(new Vec2(1f, -1f)), 1e-3f);
            Assert.IsTrue(Vec2.Approximately(Vec2.Up, MathUtil.Rotate(Vec2.Right, 90f), Tol));
            Assert.AreEqual(90f, MathUtil.AngleBetween(Vec2.Right, Vec2.Up), 1e-3f);
            Assert.AreEqual(0f, MathUtil.AngleBetween(Vec2.Zero, Vec2.Up));
        }

        [Test]
        public void MathUtil_Scalars()
        {
            Assert.AreEqual(1f, MathUtil.Clamp(5f, 0f, 1f));
            Assert.AreEqual(-2, MathUtil.Clamp(-7, -2, 3));
            Assert.AreEqual(0.25f, MathUtil.Lerp(0f, 1f, 0.25f), Tol);
            Assert.IsTrue(MathUtil.Approximately(1f, 1.0005f, 0.001f));
            Assert.IsFalse(MathUtil.IsFinite(float.NaN));
            Assert.AreEqual(3, MathUtil.RoundToInt(2.5f));
            Assert.AreEqual(-3, MathUtil.RoundToInt(-2.5f));
            Assert.AreEqual(2, MathUtil.RoundToInt(2.49f));
        }

        [Test]
        public void SimTime_SecondsToTicks_RoundsToNearest()
        {
            Assert.AreEqual(42, SimTime.SecondsToTicks(1.4f));
            Assert.AreEqual(90, SimTime.SecondsToTicks(3f));
            Assert.AreEqual(33, SimTime.SecondsToTicks(1.1f));
            Assert.AreEqual(27, SimTime.SecondsToTicks(0.9f));
            Assert.AreEqual(1, SimTime.SecondsToTicks(0.02f));
            Assert.AreEqual(0, SimTime.SecondsToTicks(-1f));
            Assert.AreEqual(1f / 30f, SimTime.Dt, 1e-7f);
        }
    }
}
