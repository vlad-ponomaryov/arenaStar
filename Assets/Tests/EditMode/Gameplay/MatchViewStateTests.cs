using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Gameplay.Match;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class MatchViewStateTests
    {
        [Test]
        public void InterpolatesBetweenTheTwoLatestTicks()
        {
            MatchSimulation sim = TestMatchFactory.CreateSingle();
            var view = new MatchViewState();
            view.Reset(sim.State);
            Vec2 start = sim.State.Fighters[0].Position;
            Assert.AreEqual(start, view.Position(0, 0.7f));

            PlayerInput[] inputs = TestMatchFactory.Inputs();
            inputs[0] = new PlayerInput(new Vec2(1f, 0f), Vec2.Zero, InputButtons.None);
            sim.Tick(inputs);
            view.Capture(sim.State);
            Vec2 after = sim.State.Fighters[0].Position;

            Assert.AreEqual(start, view.Position(0, 0f));
            Assert.AreEqual(after, view.Position(0, 1f));
            Assert.IsTrue(Vec2.Approximately(Vec2.Lerp(start, after, 0.5f), view.Position(0, 0.5f), 1e-6f));
            Assert.AreEqual(new Vec2(1f, 0f), view.Facing(0));
            Assert.IsTrue(view.Alive(0));
        }

        [Test]
        public void Capture_DoesNotAllocate()
        {
            MatchSimulation sim = TestMatchFactory.CreateSingle();
            var view = new MatchViewState();
            view.Reset(sim.State);
            view.Capture(sim.State);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++) view.Capture(sim.State);
            });
        }
    }
}
