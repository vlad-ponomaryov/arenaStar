using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Gameplay.Input;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class LocalInputCollectorTests
    {
        [Test]
        public void AttackPressedBetweenTicks_AppearsInExactlyOneInput_WithItsAim()
        {
            var c = new LocalInputCollector();
            c.PressAttack(new Vec2(0f, 0.5f), false);

            PlayerInput first = c.ConsumeForTick();
            Assert.IsTrue(first.Has(InputButtons.Attack));
            Assert.IsFalse(first.Has(InputButtons.AutoAim));
            Assert.AreEqual(new Vec2(0f, 0.5f), first.Aim, "the aim of the release, though the stick is no longer held");

            for (int i = 0; i < 3; i++)
            {
                PlayerInput next = c.ConsumeForTick();
                Assert.AreEqual(InputButtons.None, next.Buttons, "tick " + i);
                Assert.AreEqual(Vec2.Zero, next.Aim);
            }
        }

        [Test]
        public void SeveralPressesBeforeOneTick_CountAsOne()
        {
            var c = new LocalInputCollector();
            c.PressTrick();
            c.PressTrick();
            Assert.IsTrue(c.ConsumeForTick().Has(InputButtons.Trick));
            Assert.IsFalse(c.ConsumeForTick().Has(InputButtons.Trick));
        }

        [Test]
        public void PressRightAfterAPress_IsDelayedOneTick_SoTheSimulationSeesARisingEdge()
        {
            var c = new LocalInputCollector();
            c.PressAttack(Vec2.Zero, true);
            Assert.IsTrue(c.ConsumeForTick().Has(InputButtons.Attack));

            c.PressAttack(Vec2.Zero, true);
            Assert.IsFalse(c.ConsumeForTick().Has(InputButtons.Attack), "released for one tick");
            PlayerInput third = c.ConsumeForTick();
            Assert.IsTrue(third.Has(InputButtons.Attack));
            Assert.IsTrue(third.Has(InputButtons.AutoAim));
        }

        [Test]
        public void TapAttack_SetsAutoAim_AndZeroAim()
        {
            var c = new LocalInputCollector();
            c.SetAim(InputChannel.Touch, new Vec2(1f, 0f));
            c.PressAttack(new Vec2(1f, 0f), true);
            PlayerInput input = c.ConsumeForTick();
            Assert.AreEqual(InputButtons.Attack | InputButtons.AutoAim, input.Buttons);
            Assert.AreEqual(Vec2.Zero, input.Aim);
        }

        [Test]
        public void AttackAndUltInOneTick_AttackFirst_UltNext()
        {
            var c = new LocalInputCollector();
            c.PressUlt(new Vec2(-1f, 0f), false);
            c.PressAttack(new Vec2(0f, 1f), false);

            PlayerInput a = c.ConsumeForTick();
            Assert.AreEqual(InputButtons.Attack, a.Buttons);
            Assert.AreEqual(new Vec2(0f, 1f), a.Aim);

            PlayerInput b = c.ConsumeForTick();
            Assert.AreEqual(InputButtons.Ult, b.Buttons);
            Assert.AreEqual(new Vec2(-1f, 0f), b.Aim);
        }

        [Test]
        public void Team1_ViewDirections_AreRotated180()
        {
            var c = new LocalInputCollector { ViewRotated = true };
            c.SetMove(InputChannel.Touch, new Vec2(0.6f, 0.8f));
            c.SetAim(InputChannel.Touch, new Vec2(0f, 1f));
            PlayerInput held = c.ConsumeForTick();
            Assert.AreEqual(new Vec2(-0.6f, -0.8f), held.Move);
            Assert.AreEqual(new Vec2(0f, -1f), held.Aim);

            c.PressAttack(new Vec2(1f, 0f), false);
            Assert.AreEqual(new Vec2(-1f, 0f), c.ConsumeForTick().Aim);

            c.ConsumeForTick(); // a release tick between two attacks
            c.PressAttackSim(new Vec2(1f, 0f), false);
            Assert.AreEqual(new Vec2(1f, 0f), c.ConsumeForTick().Aim, "simulation-space aim (mouse ray) is not rotated");
            Assert.AreEqual(new Vec2(0.5f, 0f), c.SimToView(c.ViewToSim(new Vec2(0.5f, 0f))));
        }

        [Test]
        public void Team0_ViewDirections_AreUnchanged()
        {
            var c = new LocalInputCollector();
            c.SetMove(InputChannel.Touch, new Vec2(0.6f, 0.8f));
            Assert.AreEqual(new Vec2(0.6f, 0.8f), c.ConsumeForTick().Move);
        }

        [Test]
        public void TouchChannel_WinsOverKeyboard_WhenActive()
        {
            var c = new LocalInputCollector();
            c.SetMove(InputChannel.KeyboardMouse, new Vec2(1f, 0f));
            Assert.AreEqual(new Vec2(1f, 0f), c.Move);
            c.SetMove(InputChannel.Touch, new Vec2(0f, -1f));
            Assert.AreEqual(new Vec2(0f, -1f), c.Move);
            c.SetMove(InputChannel.Touch, Vec2.Zero);
            Assert.AreEqual(new Vec2(1f, 0f), c.Move);
        }

        [Test]
        public void LongVectors_AreClampedToUnitLength()
        {
            var c = new LocalInputCollector();
            c.SetMove(InputChannel.Touch, new Vec2(3f, 4f));
            Assert.AreEqual(1f, c.Move.Length, 1e-5f);
        }

        [Test]
        public void Clear_DropsLatchedPresses()
        {
            var c = new LocalInputCollector();
            c.PressAttack(Vec2.Zero, true);
            c.PressTrick();
            c.SetMove(InputChannel.Touch, new Vec2(1f, 0f));
            c.Clear();
            PlayerInput input = c.ConsumeForTick();
            Assert.AreEqual(InputButtons.None, input.Buttons);
            Assert.AreEqual(Vec2.Zero, input.Move);
        }

        [Test]
        public void ConsumeForTick_DoesNotAllocate()
        {
            var c = new LocalInputCollector();
            c.ConsumeForTick();
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    c.SetMove(InputChannel.Touch, new Vec2(0.5f, 0.5f));
                    c.PressAttack(new Vec2(0f, 1f), false);
                    c.PressTrick();
                    c.ConsumeForTick();
                }
            });
        }
    }
}
