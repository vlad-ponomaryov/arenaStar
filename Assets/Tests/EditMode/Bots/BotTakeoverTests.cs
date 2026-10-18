using Arena.Bots;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Net.Server.Match;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Bots
{
    /// <summary>
    /// Switching a bot on and off a slot (Server-Spec §7, S3.6): <see cref="BotController.SetBotActive"/> starts a brain from
    /// scratch (no memory, nothing seen), hands the slot back, does not allocate; <see cref="BotTakeover"/> reasons and timers.
    /// </summary>
    public sealed class BotTakeoverTests
    {
        [Test]
        public void SetBotActive_OnOff_ResetsTheBrain_NoAllocations()
        {
            MatchSimulation sim = TestMatchFactory.CreateWith(TestMatchFactory.CreateDefs(),
                TestMatchFactory.Slot(0, 0, 10.5f, 12.5f), TestMatchFactory.Slot(0, 1, 10.5f, 18.5f));
            var bots = new BotController(BotTestFactory.Behaviour());
            var inputs = TestMatchFactory.Inputs();
            bots.SetBotActive(0, true, BotTestFactory.Novice(), 5, 0);
            Assert.IsTrue(bots.IsBot(0));
            Assert.AreEqual(1, bots.BotCount);
            for (int t = 0; t < 40; t++) BotTestFactory.Step(sim, bots, inputs);
            Assert.AreNotEqual(-1, bots.FirstSeenTick(0, 1), "the bot sees the enemy");

            bots.SetBotActive(0, false, null, 5, 0);
            Assert.IsFalse(bots.IsBot(0));
            Assert.AreEqual(0, bots.BotCount);
            Assert.AreEqual(-1, bots.FirstSeenTick(0, 1), "memory dropped");
            inputs[0] = new PlayerInput(new Vec2(1f, 0f), Vec2.Zero, InputButtons.None);
            bots.Update(sim.State, sim.Defs, sim.Events, inputs);
            Assert.AreEqual(1f, inputs[0].Move.X, "an inactive slot's input is untouched");

            var difficulty = BotTestFactory.Novice();
            AllocationAssert.NoGcAllocations(() =>
            {
                bots.SetBotActive(0, true, difficulty, 5, 1);
                bots.SetBotActive(0, false, difficulty, 5, 0);
                bots.SetBotActive(0, true, difficulty, 5, 2);
            });
            Assert.IsTrue(bots.IsBot(0));
        }

        [Test]
        public void AddBot_IsTakeoverZero()
        {
            var a = new BotController(BotTestFactory.Behaviour());
            var b = new BotController(BotTestFactory.Behaviour());
            a.AddBot(3, BotTestFactory.Novice(), 77);
            b.SetBotActive(3, true, BotTestFactory.Novice(), 77, 0);
            Assert.AreEqual(a.GetBrain(3).Rng, b.GetBrain(3).Rng, "offline bots keep their seeds");
            b.SetBotActive(3, true, BotTestFactory.Novice(), 77, 1);
            Assert.AreNotEqual(a.GetBrain(3).Rng, b.GetBrain(3).Rng, "another takeover — another stream");
        }

        [Test]
        public void Takeover_NoInput_Afk_Reasons()
        {
            var bots = new BotController(BotTestFactory.Behaviour());
            var t = new BotTakeover(bots, BotTestFactory.Novice(), 1, takeoverTicks: 3, afkTicks: 5);
            PlayerInput move = new PlayerInput(new Vec2(0f, 1f), Vec2.Zero, InputButtons.None);
            t.Observe(0, true, in move);
            t.Observe(0, false, in move);
            t.Observe(0, false, in move);
            Assert.IsFalse(t.IsBot(0));
            t.Observe(0, false, in move);
            Assert.AreEqual(BotControlReason.NoInput, t.ReasonsOf(0));
            Assert.IsTrue(bots.IsBot(0));
            t.Observe(0, true, in move);
            Assert.IsFalse(t.IsBot(0), "a real input returns the control in the same tick");
            Assert.IsFalse(bots.IsBot(0));

            PlayerInput idle = default;
            for (int i = 0; i < 4; i++) t.Observe(0, true, in idle);
            Assert.IsFalse(t.IsBot(0));
            t.Observe(0, true, in idle);
            Assert.AreEqual(BotControlReason.Afk, t.ReasonsOf(0));
            t.Observe(0, true, in move);
            Assert.IsFalse(t.IsBot(0));
            Assert.IsTrue(t.WasAfk(0));

            t.Set(0, BotControlReason.Left);
            t.Observe(0, true, in move);
            Assert.IsTrue(t.IsBot(0), "Leave holds");
            Assert.AreEqual(3, t.TakeoverCount, "no input, AFK, leave");
        }
    }
}
