using Arena.UI.Hud;
using NUnit.Framework;

namespace Arena.Tests.EditMode.UI
{
    public sealed class ScoreBarViewTests
    {
        [Test]
        public void CeilSeconds_CountsDownTwelveToOne()
        {
            Assert.AreEqual(12, ScoreBarView.CeilSeconds(360));
            Assert.AreEqual(12, ScoreBarView.CeilSeconds(331));
            Assert.AreEqual(11, ScoreBarView.CeilSeconds(330));
            Assert.AreEqual(1, ScoreBarView.CeilSeconds(1));
            Assert.AreEqual(0, ScoreBarView.CeilSeconds(0));
        }
    }
}
