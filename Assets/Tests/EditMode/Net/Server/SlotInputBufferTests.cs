using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Net.Server.Match;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Server
{
    /// <summary>
    /// The server's per-slot input buffer (Server-Spec §2.3, §2.4, S3.6): inputs on their ticks, first copy wins, the window,
    /// repeat without buttons then zero, late presses carried to the next rising edge (one aimed press per tick, attack before
    /// ult), countdown ticks skipped, the margin minimum over the window, no allocations.
    /// </summary>
    public sealed class SlotInputBufferTests
    {
        private const int Ahead = 15;
        private const int Repeat = 6;

        private static PlayerInput Move(float x, InputButtons buttons = InputButtons.None, Vec2 aim = default) =>
            new PlayerInput(new Vec2(x, 0f), aim, buttons);

        [Test]
        public void InputsApplyOnTheirTicks_FirstCopyWins()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.Receive(5, Move(0.1f), 5);
            b.Receive(6, Move(0.2f), 5);
            b.Receive(6, Move(0.9f), 5);
            b.Receive(7, Move(0.3f), 5);
            Assert.AreEqual(0.1f, b.Take(5, out bool r5).Move.X);
            Assert.IsTrue(r5);
            Assert.AreEqual(0.2f, b.Take(6, out _).Move.X, "first copy wins");
            Assert.AreEqual(0.3f, b.Take(7, out _).Move.X);
            Assert.AreEqual(7, b.LastAppliedInputTick);
            Assert.AreEqual(1, b.Duplicates);
        }

        [Test]
        public void Window_TooFarAheadIsDropped()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.Receive(10 + Ahead, Move(0.5f), 10);
            b.Receive(10 + Ahead + 1, Move(0.7f), 10);
            Assert.AreEqual(1, b.TooFarAhead);
            for (int t = 10; t < 10 + Ahead; t++) b.Take(t, out _);
            Assert.AreEqual(0.5f, b.Take(10 + Ahead, out bool real).Move.X);
            Assert.IsTrue(real);
            b.Take(10 + Ahead + 1, out bool real2);
            Assert.IsFalse(real2, "the dropped input is not applied later");
        }

        [Test]
        public void Missing_RepeatsMoveAndAimWithoutButtons_ThenZero()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.Receive(0, Move(0.5f, InputButtons.Attack | InputButtons.AutoAim, new Vec2(0f, 0.5f)), 0);
            Assert.IsTrue(b.Take(0, out _).Has(InputButtons.Attack));
            for (int t = 1; t <= Repeat; t++)
            {
                PlayerInput p = b.Take(t, out bool real);
                Assert.IsFalse(real);
                Assert.AreEqual(0.5f, p.Move.X, "move repeats");
                Assert.AreEqual(0.5f, p.Aim.Y, "aim repeats");
                Assert.AreEqual(InputButtons.None, p.Buttons, "no buttons in a repeat");
            }
            Assert.AreEqual(default(PlayerInput), b.Take(Repeat + 1, out _), "then a zero input");
            Assert.AreEqual(Repeat + 1, b.MissingTicks);
            Assert.AreEqual(0, b.LastAppliedInputTick, "a repeat is not an applied input");
        }

        [Test]
        public void LatePress_LosesMovement_AndIsAppliedAtTheNextTick()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.Receive(0, Move(0f), 0);
            b.Take(0, out _);
            b.Take(1, out bool real1); // tick 1 missing
            Assert.IsFalse(real1);
            var aim = new Vec2(0.6f, 0f);
            b.Receive(1, Move(1f, InputButtons.Attack, aim), 2); // arrives late, while T = 2
            b.Receive(2, Move(-0.3f), 2);
            Assert.AreEqual(1, b.LateInputs);
            Assert.AreEqual(1, b.PendingCount);
            PlayerInput p = b.Take(2, out bool real2);
            Assert.IsTrue(real2);
            Assert.AreEqual(-0.3f, p.Move.X, "movement of the late input is dropped");
            Assert.IsTrue(p.Has(InputButtons.Attack), "the press is carried");
            Assert.AreEqual(aim, p.Aim, "with its aim");
            Assert.AreEqual(0, b.PendingCount);
            b.Receive(1, Move(1f, InputButtons.Attack, aim), 3); // another copy of the late tick
            Assert.AreEqual(1, b.PressesCarried, "a late tick is carried once");
        }

        [Test]
        public void LatePress_WaitsForARisingEdge()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.Take(0, out _); // missing
            b.Receive(1, Move(0f, InputButtons.Attack), 1);
            b.Receive(0, Move(0f, InputButtons.Attack), 1); // late press of tick 0
            Assert.IsTrue(b.Take(1, out _).Has(InputButtons.Attack), "tick 1 has its own press");
            Assert.AreEqual(1, b.PendingCount, "the late press waits: the button is in this tick");
            b.Receive(2, Move(0f), 2);
            Assert.IsFalse(b.Take(2, out _).Has(InputButtons.Attack), "no edge right after a pressed tick");
            b.Receive(3, Move(0f), 3);
            Assert.IsTrue(b.Take(3, out _).Has(InputButtons.Attack), "applied at the next rising edge");
            Assert.AreEqual(0, b.PendingCount);
        }

        [Test]
        public void LatePresses_OneAimedPressPerTick_AttackBeforeUlt()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.Take(0, out _);
            var aimA = new Vec2(0.5f, 0f);
            var aimU = new Vec2(0f, -0.5f);
            b.Receive(0, new PlayerInput(Vec2.Zero, aimA, InputButtons.Attack | InputButtons.Ult | InputButtons.Trick), 1);
            Assert.AreEqual(3, b.PendingCount);
            PlayerInput p1 = b.Take(1, out _);
            Assert.IsTrue(p1.Has(InputButtons.Attack));
            Assert.IsFalse(p1.Has(InputButtons.Ult), "one aim per tick");
            Assert.IsTrue(p1.Has(InputButtons.Trick), "the trick has no aim");
            Assert.AreEqual(aimA, p1.Aim);
            PlayerInput p2 = b.Take(2, out _);
            Assert.IsTrue(p2.Has(InputButtons.Ult), "the ult follows");
            Assert.AreEqual(aimA, p2.Aim, "with the aim it was pressed with");
            Assert.AreEqual(0, b.PendingCount);

            // An ult that conflicts with a fresh attack of the next tick waits one tick.
            var c = new SlotInputBuffer(Ahead, Repeat);
            c.Take(0, out _);
            c.Receive(0, new PlayerInput(Vec2.Zero, aimU, InputButtons.Ult | InputButtons.AutoAim), 1);
            c.Receive(1, new PlayerInput(Vec2.Zero, aimA, InputButtons.Attack), 1);
            PlayerInput q1 = c.Take(1, out _);
            Assert.IsTrue(q1.Has(InputButtons.Attack));
            Assert.IsFalse(q1.Has(InputButtons.Ult));
            Assert.AreEqual(aimA, q1.Aim);
            PlayerInput q2 = c.Take(2, out _);
            Assert.IsTrue(q2.Has(InputButtons.Ult));
            Assert.IsTrue(q2.Has(InputButtons.AutoAim), "AutoAim travels with its press");
            Assert.AreEqual(aimU, q2.Aim);
        }

        [Test]
        public void PendingQueue_IsLimited()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            for (int t = 0; t < 4; t++) b.Take(t, out _);
            for (int t = 0; t < 4; t++) b.Receive(t, Move(0f, InputButtons.Attack), 4);
            Assert.AreEqual(Arena.Net.NetLimits.MaxPendingPresses, b.PendingCount);
            Assert.AreEqual(1, b.PressesDropped);
        }

        [Test]
        public void Countdown_SkippedTicksAreNeverApplied()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.Reset(-3);
            for (int t = -3; t < 3; t++) b.Receive(t, Move(0.4f, InputButtons.Attack), -3);
            b.Skip(-3);
            b.Skip(-2);
            b.Skip(-1);
            Assert.AreEqual(-1, b.LastAppliedInputTick, "countdown inputs are acknowledged");
            b.Receive(-2, Move(0.4f, InputButtons.Ult), 0); // late copy of a countdown tick
            Assert.AreEqual(0, b.PendingCount, "presses of the countdown are not carried");
            PlayerInput p = b.Take(0, out bool real);
            Assert.IsTrue(real);
            Assert.AreEqual(0.4f, p.Move.X);
            Assert.AreEqual(0, b.LateInputs);
        }

        [Test]
        public void InputMarginMin_IsTheMinimumOverTheWindow()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            b.RecordPacket(13, 10);
            b.RecordPacket(11, 10);
            Assert.AreEqual(1, b.InputMarginMin(10, 30));
            b.RecordPacket(25, 20);
            Assert.AreEqual(1, b.InputMarginMin(20, 30), "the old minimum is still in the window");
            Assert.AreEqual(5, b.InputMarginMin(45, 30), "it left the window");
            Assert.AreEqual(5, b.InputMarginMin(80, 30), "no packets in the window — the last value");
            b.RecordPacket(78, 80);
            Assert.AreEqual(-2, b.InputMarginMin(80, 30), "late packets give a negative margin");
        }

        [Test]
        public void Buffer_DoesNotAllocate()
        {
            var b = new SlotInputBuffer(Ahead, Repeat);
            PlayerInput press = Move(0.3f, InputButtons.Attack);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int t = 0; t < 500; t++)
                {
                    b.RecordPacket(t + 2, t);
                    if (t % 7 != 0) b.Receive(t + 2, press, t);
                    if (t % 7 == 3) b.Receive(t - 1, press, t);
                    b.Take(t, out _);
                    b.InputMarginMin(t, 30);
                }
            });
        }
    }
}
