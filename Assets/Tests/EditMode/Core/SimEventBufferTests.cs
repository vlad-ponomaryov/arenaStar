using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class SimEventBufferTests
    {
        [Test]
        public void Add_Read_Clear()
        {
            var buffer = new SimEventBuffer();
            Assert.IsTrue(buffer.Add(new SimEvent(SimEventType.ChargeRestored, 7, 2, -1, 3, new Vec2(1f, 2f), Vec2.Zero)));
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(SimEventType.ChargeRestored, buffer[0].Type);
            Assert.AreEqual(7, buffer[0].Tick);
            Assert.AreEqual(3, buffer[0].Value);
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void Overflow_DropsAndCounts_WithoutException()
        {
            var buffer = new SimEventBuffer();
            var e = new SimEvent(SimEventType.FighterSpawned, 0, 0, -1, 0, Vec2.Zero, Vec2.Zero);
            for (int i = 0; i < SimLimits.MaxEvents; i++) Assert.IsTrue(buffer.Add(e));
            Assert.IsFalse(buffer.Add(e));
            Assert.AreEqual(SimLimits.MaxEvents, buffer.Count);
            Assert.AreEqual(1, buffer.Dropped);
        }

        [Test]
        public void Tick_ClearsEventsOfPreviousTick()
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(2));
            Assert.AreEqual(2, sim.Events.Count, "spawn events before the first tick");
            sim.Tick(TestMatchFactory.Inputs());
            Assert.AreEqual(0, sim.Events.CountOf(SimEventType.FighterSpawned));
        }
    }
}
