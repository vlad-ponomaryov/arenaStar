using Arena.UI.Common;
using Arena.UI.Hud;
using NUnit.Framework;

namespace Arena.Tests.EditMode.UI
{
    public sealed class NumberStringsTests
    {
        [Test]
        public void Numbers_AndClock_AreFormatted_AndCached()
        {
            Assert.AreEqual("0", NumberStrings.Get(0));
            Assert.AreEqual("12", NumberStrings.Get(12));
            Assert.AreSame(NumberStrings.Get(12), NumberStrings.Get(12));
            Assert.AreEqual("-3", NumberStrings.Get(-3));
            Assert.AreEqual("2:30", NumberStrings.Clock(150));
            Assert.AreEqual("0:05", NumberStrings.Clock(5));
            Assert.AreEqual("0:00", NumberStrings.Clock(-1));
            Assert.AreSame(NumberStrings.Clock(150), NumberStrings.Clock(150));
        }

        [Test]
        public void CeilSeconds_CountsDownTwelveToOne()
        {
            Assert.AreEqual(12, ScoreBarView.CeilSeconds(360));
            Assert.AreEqual(12, ScoreBarView.CeilSeconds(331));
            Assert.AreEqual(11, ScoreBarView.CeilSeconds(330));
            Assert.AreEqual(1, ScoreBarView.CeilSeconds(1));
            Assert.AreEqual(0, ScoreBarView.CeilSeconds(0));
        }

        [Test]
        public void CachedLookups_DoNotAllocate()
        {
            NumberStrings.Get(7);
            NumberStrings.Clock(99);
            Arena.Tests.EditMode.Core.AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    NumberStrings.Get(7);
                    NumberStrings.Clock(99);
                }
            });
        }
    }
}
