using Arena.Core.Random;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class DeterministicRngTests
    {
        [Test]
        public void SameSeed_SameSequence()
        {
            var a = DeterministicRng.FromSeed(42);
            var b = DeterministicRng.FromSeed(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong(), "step " + i);
        }

        [Test]
        public void DifferentSeeds_DifferentSequences()
        {
            var a = DeterministicRng.FromSeed(1);
            var b = DeterministicRng.FromSeed(2);
            int same = 0;
            for (int i = 0; i < 100; i++)
                if (a.NextUInt() == b.NextUInt()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void ZeroSeed_IsValid()
        {
            var r = DeterministicRng.FromSeed(0);
            Assert.IsFalse(r.S0 == 0UL && r.S1 == 0UL);
            Assert.AreNotEqual(r.NextULong(), r.NextULong());
        }

        [Test]
        public void CopiedStruct_ForksSameSequence()
        {
            var a = DeterministicRng.FromSeed(7);
            a.NextULong();
            var copy = a;
            Assert.AreEqual(a.NextUInt(), copy.NextUInt());
            Assert.IsTrue(a.Equals(copy));
        }

        [Test]
        public void RangeInt_StaysInBounds_AndHitsBothEnds()
        {
            var r = DeterministicRng.FromSeed(123);
            bool sawMin = false, sawMax = false;
            for (int i = 0; i < 10000; i++)
            {
                int v = r.Range(-3, 4);
                Assert.GreaterOrEqual(v, -3);
                Assert.Less(v, 4);
                if (v == -3) sawMin = true;
                if (v == 3) sawMax = true;
            }
            Assert.IsTrue(sawMin && sawMax);
            Assert.AreEqual(5, r.Range(5, 5));
            Assert.AreEqual(5, r.Range(5, 2));
            Assert.AreEqual(int.MinValue, DeterministicRng.FromSeed(1).Range(int.MinValue, int.MinValue + 1));
        }

        [Test]
        public void RangeFloat_And_Float01_StayInBounds()
        {
            var r = DeterministicRng.FromSeed(99);
            float min = 1f, max = 0f;
            for (int i = 0; i < 10000; i++)
            {
                float u = r.NextFloat01();
                Assert.GreaterOrEqual(u, 0f);
                Assert.Less(u, 1f);
                float v = r.Range(-2.5f, 2.5f);
                Assert.GreaterOrEqual(v, -2.5f);
                Assert.Less(v, 2.5f);
                if (u < min) min = u;
                if (u > max) max = u;
            }
            Assert.Less(min, 0.01f);
            Assert.Greater(max, 0.99f);
            Assert.AreEqual(3f, r.Range(3f, 3f));
        }
    }
}
