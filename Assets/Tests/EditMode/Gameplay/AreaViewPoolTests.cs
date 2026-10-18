using Arena.Core.Defs;
using Arena.Core.State;
using Arena.Gameplay.View;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>Lob flight of the area views (S2.11): progress over the delay, arc from the launch height to the ground.</summary>
    public sealed class AreaViewPoolTests
    {
        private static AreaState Lob(int spawn, int activate) =>
            new AreaState { Active = true, Kind = AreaKind.Burst, SpawnTick = spawn, ActivateTick = activate };

        [Test]
        public void FlightProgress_GoesFrom0To1_OverTheDelay()
        {
            AreaState a = Lob(10, 25);
            Assert.AreEqual(0f, AreaViewPool.FlightProgress(in a, 10, 0f), 1e-6f);
            Assert.AreEqual(0.5f, AreaViewPool.FlightProgress(in a, 17, 0.5f), 1e-6f);
            Assert.AreEqual(1f, AreaViewPool.FlightProgress(in a, 30, 0f), 1e-6f);
        }

        [Test]
        public void FlightProgress_NoBomb_ForTriggeredOrPlacedAreas()
        {
            AreaState burst = Lob(10, 25);
            burst.Triggered = true;
            Assert.Less(AreaViewPool.FlightProgress(in burst, 12, 0f), 0f);
            AreaState mine = Lob(10, 10);
            mine.Kind = AreaKind.Mine;
            Assert.Less(AreaViewPool.FlightProgress(in mine, 12, 0f), 0f);
            AreaState instant = Lob(10, 10);
            Assert.Less(AreaViewPool.FlightProgress(in instant, 10, 0f), 0f, "no flight time");
        }

        [Test]
        public void ArcHeight_StartsAtLaunch_PeaksInTheMiddle_LandsOnGround()
        {
            Assert.AreEqual(0.8f, AreaViewPool.ArcHeight(0f, 0.8f, 2f), 1e-6f);
            Assert.AreEqual(0.4f + 2f, AreaViewPool.ArcHeight(0.5f, 0.8f, 2f), 1e-6f);
            Assert.AreEqual(0f, AreaViewPool.ArcHeight(1f, 0.8f, 2f), 1e-6f);
        }
    }
}
