using Arena.Gameplay.Feel;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class NumberStringCacheTests
    {
        [Test]
        public void Numbers_Percents_AndClock_AreFormatted_AndCached()
        {
            Assert.AreEqual("0", NumberStringCache.Get(0));
            Assert.AreEqual("12", NumberStringCache.Get(12));
            Assert.AreEqual("5600", NumberStringCache.Get(5600));
            Assert.AreSame(NumberStringCache.Get(12), NumberStringCache.Get(12));
            Assert.AreSame(NumberStringCache.Get(1200), NumberStringCache.Get(1200), "damage numbers are cached too");
            Assert.AreEqual("-3", NumberStringCache.Get(-3));
            Assert.AreEqual("75%", NumberStringCache.Percent(75));
            Assert.AreSame(NumberStringCache.Percent(75), NumberStringCache.Percent(75));
            Assert.AreEqual("2:30", NumberStringCache.Clock(150));
            Assert.AreEqual("0:05", NumberStringCache.Clock(5));
            Assert.AreEqual("0:00", NumberStringCache.Clock(-1));
            Assert.AreSame(NumberStringCache.Clock(150), NumberStringCache.Clock(150));
        }

        [Test]
        public void CachedLookups_AfterWarmUp_DoNotAllocate()
        {
            NumberStringCache.Prewarm(100);
            NumberStringCache.Get(3400);
            NumberStringCache.Percent(50);
            NumberStringCache.Clock(99);
            Arena.Tests.EditMode.Core.AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    NumberStringCache.Get(i);
                    NumberStringCache.Get(3400);
                    NumberStringCache.Percent(50);
                    NumberStringCache.Clock(99);
                }
            });
        }
    }
}
