using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Gameplay.Input;
using NUnit.Framework;
using UnityEngine.InputSystem;
using PlayerInput = Arena.Core.Input.PlayerInput;

namespace Arena.Tests.EditMode.Gameplay
{
    /// <summary>F1: WASD reaches the tick input through <see cref="KeyboardMouseInput"/> and <see cref="LocalInputCollector"/>.</summary>
    public sealed class KeyboardMouseInputTests : InputTestFixture
    {
        private Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [Test]
        public void Wasd_MovesTheTickInput()
        {
            var collector = new LocalInputCollector();
            var kbm = new KeyboardMouseInput();

            Press(_keyboard.wKey);
            kbm.Poll(collector, null, Vec2.Zero, 5f, 5f);
            PlayerInput up = collector.ConsumeForTick();
            Assert.AreEqual(1f, up.Move.Y, 1e-4f);
            Assert.AreEqual(0f, up.Move.X, 1e-4f);

            Press(_keyboard.dKey);
            kbm.Poll(collector, null, Vec2.Zero, 5f, 5f);
            PlayerInput diag = collector.ConsumeForTick();
            Assert.AreEqual(1f, diag.Move.Length, 1e-4f, "normalized");
            Assert.Greater(diag.Move.X, 0f);

            Release(_keyboard.wKey);
            Release(_keyboard.dKey);
            kbm.Poll(collector, null, Vec2.Zero, 5f, 5f);
            Assert.AreEqual(Vec2.Zero, collector.ConsumeForTick().Move);
        }

        [Test]
        public void Wasd_IsNotZeroedByAnIdleStick_AndTurnsForTeam1()
        {
            var collector = new LocalInputCollector { ViewRotated = true };
            var kbm = new KeyboardMouseInput();
            collector.SetMove(InputChannel.Touch, Vec2.Zero); // the HUD writes the released stick every frame
            Press(_keyboard.aKey);
            kbm.Poll(collector, null, Vec2.Zero, 5f, 5f);
            collector.SetMove(InputChannel.Touch, Vec2.Zero);
            Assert.AreEqual(1f, collector.ConsumeForTick().Move.X, 1e-4f, "A = screen left = +x for the turned camera");
        }

        [Test]
        public void SpaceAndShift_PressButtonsOnce()
        {
            var collector = new LocalInputCollector();
            var kbm = new KeyboardMouseInput();
            Press(_keyboard.spaceKey);
            kbm.Poll(collector, null, Vec2.Zero, 5f, 5f);
            Press(_keyboard.leftShiftKey); // the next frame, before the tick: both presses stay latched
            kbm.Poll(collector, null, Vec2.Zero, 5f, 5f);
            PlayerInput input = collector.ConsumeForTick();
            Assert.IsTrue(input.Has(InputButtons.Attack));
            Assert.IsTrue(input.Has(InputButtons.AutoAim));
            Assert.IsTrue(input.Has(InputButtons.Trick));
        }
    }
}
