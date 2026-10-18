using System.Collections.Generic;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.Sim.Systems;
using Arena.Core.State;
using NUnit.Framework;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Lag compensation in the simulation (Server-Spec §2.8, S3.13): the position history, the rewind of instant contact hits
    /// (cone, dash touches) and of their auto-aim by <c>PlayerInput.ViewDelayTicks</c>, its bounds, no rewind for a target that
    /// was not in play in the window, and no rewind for projectiles.
    /// </summary>
    public sealed class LagCompensationTests
    {
        private const float TargetSpeed = 4f;
        private const float BrunoX = 5f;
        private const float LaneY = 10f;

        /// <summary>Bruno (slot 0, team 0) and a fast dummy (slot 1, team 1) on one lane.</summary>
        private static MatchSimulation CreateDuel(float dummyX, float dummyY)
        {
            MatchDefs defs = A.CreateDefs();
            defs.Fighters[A.Dummy].MoveSpeed = TargetSpeed;
            return TestMatchFactory.CreateWith(defs, A.Slot(A.Bruno, 0, BrunoX, LaneY), A.Slot(A.Dummy, 1, dummyX, dummyY));
        }

        /// <summary>
        /// The dummy walks along <paramref name="move"/> until the distance to Bruno at the end of the next tick is surely beyond
        /// <paramref name="farDistance"/>; returns the dummy's positions at the end of every tick (index = tick).
        /// </summary>
        private static List<Vec2> WalkAway(MatchSimulation sim, PlayerInput[] inputs, Vec2 move, float farDistance)
        {
            var ends = new List<Vec2>();
            inputs[1] = new PlayerInput(move, Vec2.Zero, InputButtons.None);
            for (int guard = 0; guard < 300; guard++)
            {
                sim.Tick(inputs);
                ends.Add(sim.State.Fighters[1].Position);
                if (Vec2.Distance(sim.State.Fighters[1].Position, sim.State.Fighters[0].Position) >= farDistance) return ends;
            }
            Assert.Fail("the dummy never got far enough");
            return ends;
        }

        /// <summary>Smallest rewind d (≤ <paramref name="maxRewind"/>) for which the dummy was within <paramref name="nearDistance"/> at the end of tick T − d.</summary>
        private static int RewindToNear(List<Vec2> ends, int tick, Vec2 from, float nearDistance, int maxRewind)
        {
            for (int d = 1; d <= maxRewind; d++)
                if (Vec2.Distance(ends[tick - d], from) <= nearDistance) return d;
            Assert.Fail("no rewind within " + maxRewind + " ticks reaches the dummy");
            return 0;
        }

        private static int PressConeAndCountHits(MatchSimulation sim, PlayerInput[] inputs, byte viewDelay, bool autoAim)
        {
            InputButtons buttons = InputButtons.Attack | (autoAim ? InputButtons.AutoAim : InputButtons.None);
            inputs[0] = new PlayerInput(Vec2.Zero, autoAim ? Vec2.Zero : Vec2.Right, buttons, viewDelay);
            sim.Tick(inputs);
            inputs[0] = default;
            int hits = 0;
            for (int i = 0; i < sim.Events.Count; i++)
            {
                SimEvent e = sim.Events[i];
                if (e.Type == SimEventType.Damaged && e.Target == 1) hits++;
            }
            return hits;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Cone_HitsWhereTheAttackerSawTheTarget_MissesWithoutRewind(bool autoAim)
        {
            const float far = 3.4f; // beyond the reach 2.5 + 0.5
            const float near = 2.3f; // within the range (auto-aim looks at the centre distance ≤ 2.5)
            int rewind = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                MatchSimulation sim = CreateDuel(BrunoX + 1.5f, LaneY);
                PlayerInput[] inputs = TestMatchFactory.Inputs();
                List<Vec2> ends = WalkAway(sim, inputs, Vec2.Right, far);
                int tick = sim.State.Tick;
                if (pass == 0) rewind = RewindToNear(ends, tick, sim.State.Fighters[0].Position, near, 10);
                byte delay = pass == 0 ? (byte)rewind : (byte)0;
                int hits = PressConeAndCountHits(sim, inputs, delay, autoAim);
                if (pass == 0) Assert.AreEqual(1, hits, "with the rewind of " + rewind + " ticks the paw hits");
                else Assert.AreEqual(0, hits, "without rewind the target is already out of reach");
            }
        }

        [Test]
        public void Cone_RewindsOnlyTheTarget_TheTargetIsNotMoved()
        {
            MatchSimulation sim = CreateDuel(BrunoX + 1.5f, LaneY);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            WalkAway(sim, inputs, Vec2.Right, 3.4f);
            int hits = PressConeAndCountHits(sim, inputs, 10, false);
            Assert.AreEqual(1, hits);
            float x = sim.State.Fighters[1].Position.X;
            Assert.Greater(x - BrunoX, 3.4f, "the state is not replaced: the target stays where it is now");
        }

        [Test]
        public void DashTouches_AreRewound()
        {
            int hitsWith = -1, hitsWithout = -1;
            for (int pass = 0; pass < 2; pass++)
            {
                // The dummy crosses the ram's lane upwards; the ram starts when it is already above the reach.
                MatchSimulation sim = CreateDuel(BrunoX + 4f, LaneY);
                PlayerInput[] inputs = TestMatchFactory.Inputs();
                inputs[1] = new PlayerInput(Vec2.Up, Vec2.Zero, InputButtons.None);
                TestMatchFactory.Run(sim, inputs, 8);
                A.ChargeUlt(sim, 0);
                byte delay = pass == 0 ? (byte)SimLimits.MaxRewindTicks : (byte)0;
                int hits = 0;
                for (int t = 0; t < A.RamTicks + 2; t++)
                {
                    inputs[0] = new PlayerInput(Vec2.Zero, Vec2.Right, t == 0 ? InputButtons.Ult : InputButtons.None, delay);
                    sim.Tick(inputs);
                    for (int i = 0; i < sim.Events.Count; i++)
                        if (sim.Events[i].Type == SimEventType.Damaged && sim.Events[i].Target == 1) hits++;
                }
                if (pass == 0) hitsWith = hits;
                else hitsWithout = hits;
            }
            Assert.AreEqual(1, hitsWith, "the ram touches the dummy where Bruno saw it");
            Assert.AreEqual(0, hitsWithout, "without rewind the dummy is out of the lane");
        }

        [Test]
        public void EffectiveRewind_IsBoundedByHistoryAndTick_AndZeroInPrediction()
        {
            MatchSimulation sim = CreateDuel(BrunoX + 1.5f, LaneY);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, 5);
            Assert.AreEqual(5, LagCompensation.EffectiveRewind(sim.State, 200), "no history before tick 0");
            TestMatchFactory.Run(sim, inputs, 30);
            Assert.AreEqual(SimLimits.MaxRewindTicks, LagCompensation.EffectiveRewind(sim.State, 200), "an inflated delay gives no more than the history");
            Assert.AreEqual(7, LagCompensation.EffectiveRewind(sim.State, 7));
            Assert.AreEqual(0, LagCompensation.EffectiveRewind(sim.State, 0));
            sim.State.Globals.Predicting = true;
            Assert.AreEqual(0, LagCompensation.EffectiveRewind(sim.State, 7), "the shadow of the client is never rewound");
        }

        [Test]
        public void History_HoldsTheEndOfEachTick()
        {
            MatchSimulation sim = CreateDuel(BrunoX + 1.5f, LaneY);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            List<Vec2> ends = WalkAway(sim, inputs, Vec2.Right, 4f);
            int tick = sim.State.Tick;
            for (int d = 1; d <= SimLimits.MaxRewindTicks; d++)
                Assert.AreEqual(ends[tick - d], LagCompensation.TargetPosition(sim.State, 1, d), "rewind " + d);
            Assert.AreEqual(sim.State.Fighters[1].Position, LagCompensation.TargetPosition(sim.State, 1, 0));
        }

        [Test]
        public void TargetThatRespawnedInTheWindow_IsNotRewound()
        {
            MatchSimulation sim = CreateDuel(BrunoX + 1.5f, LaneY);
            PlayerInput[] inputs = TestMatchFactory.Inputs();
            TestMatchFactory.Run(sim, inputs, 20);
            DamageSystem.ApplyDamage(sim.State, sim.Defs, sim.Events, 0, 1, A.DummyHealth * 2);
            Assert.IsFalse(sim.State.Fighters[1].Alive);
            int respawned = CombatTestUtil.RunUntil(sim, inputs, SimEventType.FighterSpawned, TestMatchFactory.RespawnTicks + 5);
            Assert.GreaterOrEqual(respawned, 0, "the dummy respawns");
            inputs[1] = new PlayerInput(Vec2.Right, Vec2.Zero, InputButtons.None);
            TestMatchFactory.Run(sim, inputs, 4);
            Vec2 now = sim.State.Fighters[1].Position;
            Assert.AreEqual(now, LagCompensation.TargetPosition(sim.State, 1, 8), "the window reaches back before the respawn");
            Assert.AreNotEqual(now, LagCompensation.TargetPosition(sim.State, 1, 2), "a window after the respawn is rewound");
        }

        [Test]
        public void Projectiles_AreNotRewound_StateIdenticalWhateverTheDelay()
        {
            MatchDefs defs = TestMatchFactory.CreateDefs();
            var a = TestMatchFactory.CreateWith(defs, TestMatchFactory.Slot(0, 0, 5f, 10f), TestMatchFactory.Slot(1, 1, 8f, 10f));
            var b = TestMatchFactory.CreateWith(defs, TestMatchFactory.Slot(0, 0, 5f, 10f), TestMatchFactory.Slot(1, 1, 8f, 10f));
            PlayerInput[] ia = TestMatchFactory.Inputs(), ib = TestMatchFactory.Inputs();
            for (int t = 0; t < 120; t++)
            {
                Vec2 move = (t / 15) % 2 == 0 ? Vec2.Up : new Vec2(0f, -1f);
                InputButtons buttons = t % 20 == 0 ? InputButtons.Attack | InputButtons.AutoAim : InputButtons.None;
                ia[0] = new PlayerInput(Vec2.Zero, Vec2.Zero, buttons, 0);
                ib[0] = new PlayerInput(Vec2.Zero, Vec2.Zero, buttons, (byte)SimLimits.MaxRewindTicks);
                ia[1] = ib[1] = new PlayerInput(move, Vec2.Zero, InputButtons.None);
                a.Tick(ia);
                b.Tick(ib);
            }
            Assert.Greater(a.State.Stats[0].DamageDealt, 0, "the drops did hit");
            Assert.IsTrue(a.State.ContentEquals(b.State), "a projectile (and its auto-aim) ignores ViewDelayTicks");
        }

        [Test]
        public void PlayerInput_KeepsViewDelayThroughSanitize()
        {
            var input = new PlayerInput(new Vec2(float.NaN, 0f), Vec2.Right, InputButtons.Attack, 9);
            Assert.AreEqual(9, input.Sanitized().ViewDelayTicks);
            Assert.AreNotEqual(input, new PlayerInput(input.Move, input.Aim, input.Buttons, 8));
        }
    }
}
