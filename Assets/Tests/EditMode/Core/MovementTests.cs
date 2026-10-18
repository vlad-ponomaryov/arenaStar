using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Core
{
    public sealed class MovementTests
    {
        [TestCase(2.2f)]
        [TestCase(2.5f)]
        [TestCase(2.8f)]
        public void OneSecond_MovesSpeedCells(float speed)
        {
            var sim = TestMatchFactory.CreateSingle(speed);
            var inputs = TestMatchFactory.Inputs();
            inputs[0].Move = Vec2.Right;
            Vec2 start = sim.State.Fighters[0].Position;

            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate);

            float travelled = Vec2.Distance(start, sim.State.Fighters[0].Position);
            Assert.AreEqual(speed, travelled, speed * 0.01f);
            Assert.AreEqual(speed, sim.State.Fighters[0].Velocity.Length, speed * 0.01f);
        }

        [Test]
        public void Diagonal_IsNormalized()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            inputs[0].Move = new Vec2(1f, 1f);
            Vec2 start = sim.State.Fighters[0].Position;

            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate);

            float travelled = Vec2.Distance(start, sim.State.Fighters[0].Position);
            Assert.AreEqual(TestMatchFactory.Speed, travelled, TestMatchFactory.Speed * 0.01f);
        }

        [Test]
        public void PartialStick_MovesProportionally()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            inputs[0].Move = new Vec2(0f, 0.5f);
            Vec2 start = sim.State.Fighters[0].Position;

            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate);

            Assert.AreEqual(TestMatchFactory.Speed * 0.5f, sim.State.Fighters[0].Position.Y - start.Y, 0.02f);
        }

        [Test]
        public void ArenaEdge_StopsFighterAtRadius()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            inputs[0].Move = new Vec2(-1f, -1f);

            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate * 20);

            ref FighterState f = ref sim.State.Fighters[0];
            Assert.AreEqual(TestMatchFactory.Radius, f.Position.X, 1e-4f);
            Assert.AreEqual(TestMatchFactory.Radius, f.Position.Y, 1e-4f);
            Assert.IsTrue(f.Velocity.IsZero, "velocity is the actual displacement, zero when pressed into the edge");

            inputs[0].Move = new Vec2(1f, 1f);
            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate * 30);
            Assert.AreEqual(TestMatchFactory.ArenaWidth - TestMatchFactory.Radius, f.Position.X, 1e-4f);
            Assert.AreEqual(TestMatchFactory.ArenaHeight - TestMatchFactory.Radius, f.Position.Y, 1e-4f);
        }

        [Test]
        public void NoInput_NoMovement()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            Vec2 start = sim.State.Fighters[0].Position;
            TestMatchFactory.Run(sim, inputs, 60);
            Assert.AreEqual(start, sim.State.Fighters[0].Position);
        }

        [Test]
        public void InvalidInput_IsSanitized()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            Vec2 start = sim.State.Fighters[0].Position;

            inputs[0].Move = new Vec2(float.NaN, 1f);
            TestMatchFactory.Run(sim, inputs, 10);
            Assert.AreEqual(start, sim.State.Fighters[0].Position);

            inputs[0].Move = new Vec2(100f, 0f);
            TestMatchFactory.Run(sim, inputs, SimLimits.TickRate);
            Assert.AreEqual(TestMatchFactory.Speed, sim.State.Fighters[0].Position.X - start.X, TestMatchFactory.Speed * 0.01f);
        }

        [Test]
        public void Facing_FollowsAim_ThenMovement_ThenKeeps()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            ref FighterState f = ref sim.State.Fighters[0];

            inputs[0] = new PlayerInput(Vec2.Right, new Vec2(0f, -0.3f), InputButtons.None);
            sim.Tick(inputs);
            Assert.IsTrue(Vec2.Approximately(new Vec2(0f, -1f), f.Facing, 1e-5f), "aim wins over movement");

            inputs[0] = new PlayerInput(new Vec2(-0.5f, 0f), Vec2.Zero, InputButtons.None);
            sim.Tick(inputs);
            Assert.IsTrue(Vec2.Approximately(new Vec2(-1f, 0f), f.Facing, 1e-5f), "movement when not aiming");

            inputs[0] = default;
            sim.Tick(inputs);
            Assert.IsTrue(Vec2.Approximately(new Vec2(-1f, 0f), f.Facing, 1e-5f), "kept without input");
        }

        [Test]
        public void ButtonEdge_OnlyFirstTickOfHold()
        {
            var sim = TestMatchFactory.CreateSingle();
            var inputs = TestMatchFactory.Inputs();
            ref FighterState f = ref sim.State.Fighters[0];

            inputs[0].Buttons = InputButtons.Attack;
            sim.Tick(inputs);
            Assert.IsTrue(f.WasPressed(InputButtons.Attack));
            sim.Tick(inputs);
            Assert.IsFalse(f.WasPressed(InputButtons.Attack), "held is not a new press");
            Assert.IsTrue(f.IsHeld(InputButtons.Attack));

            inputs[0].Buttons = InputButtons.None;
            sim.Tick(inputs);
            inputs[0].Buttons = InputButtons.Attack | InputButtons.AutoAim;
            sim.Tick(inputs);
            Assert.IsTrue(f.WasPressed(InputButtons.Attack));
            Assert.IsTrue(f.WasPressed(InputButtons.AutoAim));
            Assert.IsFalse(f.WasPressed(InputButtons.Ult));
        }

        [Test]
        public void Tick_WithTooFewInputs_Throws()
        {
            var sim = new MatchSimulation(TestMatchFactory.CreateDefs(), TestMatchFactory.CreateSetup(4));
            Assert.Throws<System.ArgumentException>(() => sim.Tick(new PlayerInput[2]));
        }
    }
}
