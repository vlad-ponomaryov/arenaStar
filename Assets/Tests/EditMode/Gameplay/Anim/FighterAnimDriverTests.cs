using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using Arena.Editor.Data;
using Arena.Gameplay.View.Anim;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;
using UnityEngine;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Gameplay.Anim
{
    public sealed class FighterAnimDriverTests
    {
        private const float Dt = 1f / 60f;
        private FighterAnimConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<FighterAnimConfig>();
            DataSeeder.FillFighterAnim(_config);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void AbilityFired_CallsOnShootWithSlotOfTheAbility()
        {
            MatchSimulation sim = TwoFighters();
            var driver = new FighterAnimDriver(_config, SimLimits.MaxFighters);
            var sink0 = new FakeSink();
            var sink1 = new FakeSink();
            driver.SetSink(0, sink0);
            driver.SetSink(1, sink1);

            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.AbilityFired, 1, 1, -1, MatchDefs.MakeAbilityRef(0, AbilitySlot.Ult), Vec2.Zero, Vec2.Right));
            events.Add(new SimEvent(SimEventType.AbilityFired, 1, 0, -1, MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack), Vec2.Zero, Vec2.Up));
            driver.ProcessEvents(events, sim.State);

            Assert.AreEqual(1, sink1.Shots);
            Assert.AreEqual(AbilitySlot.Ult, sink1.LastSlot);
            Assert.AreEqual(1, sink0.Shots);
            Assert.AreEqual(AbilitySlot.Attack, sink0.LastSlot);
            Assert.AreEqual(Vector3.forward, sink0.LastAim, "Core (x, y) → world (x, 0, y)");
        }

        [Test]
        public void Damaged_CallsOnHurtTowardSource_HeavyForUlt()
        {
            MatchSimulation sim = TwoFighters();
            MatchState state = sim.State;
            var driver = new FighterAnimDriver(_config, SimLimits.MaxFighters);
            var sink = new FakeSink();
            driver.SetSink(0, sink);

            var events = new SimEventBuffer();
            int ult = MatchDefs.MakeAbilityRef(1, AbilitySlot.Ult);
            events.Add(new SimEvent(SimEventType.Damaged, 1, 1, 0, 300, Vec2.Zero, Vec2.Zero, ult));
            events.Add(new SimEvent(SimEventType.Damaged, 1, 1, 0, 0, Vec2.Zero, Vec2.Zero, ult)); // 0 damage: no reaction
            driver.ProcessEvents(events, state);

            Assert.AreEqual(1, sink.Hurts);
            Assert.IsTrue(sink.LastHeavy);
            Vec2 d = state.Fighters[1].Position - state.Fighters[0].Position;
            Vector3 expected = new Vector3(d.X, 0f, d.Y);
            Assert.AreEqual(0f, Vector3.Angle(expected, sink.LastFrom), 0.01f);

            events.Clear();
            events.Add(new SimEvent(SimEventType.Damaged, 1, -1, 0, 50, Vec2.Zero, Vec2.Right, -1));
            driver.ProcessEvents(events, state);
            Assert.AreEqual(2, sink.Hurts);
            Assert.IsFalse(sink.LastHeavy);
            Assert.AreEqual(Vector3.left, sink.LastFrom, "unknown source: against the hit direction");
        }

        [Test]
        public void UpdateLife_SpawnDeathAndReveal()
        {
            var driver = new FighterAnimDriver(_config, SimLimits.MaxFighters);
            var sink = new FakeSink();
            driver.SetSink(0, sink);

            Assert.IsFalse(driver.UpdateLife(0, true, true));
            Assert.AreEqual(1, sink.Spawns, "first frame alive = spawn");
            Assert.IsFalse(driver.UpdateLife(0, true, false));
            Assert.IsFalse(driver.UpdateLife(0, true, true));
            Assert.AreEqual(1, sink.Resets, "shown again after hiding → reset");

            driver.UpdateLife(0, false, false);
            Assert.AreEqual(1, sink.Deaths, "died in view");

            // Died while hidden: no clip.
            driver.UpdateLife(0, true, false);
            driver.UpdateLife(0, false, false);
            Assert.AreEqual(1, sink.Deaths);
        }

        [Test]
        public void Dying_KeepsViewUntilDefeatEnds()
        {
            var driver = new FighterAnimDriver(_config, SimLimits.MaxFighters);
            driver.SetSink(0, new FighterAnimator(_config, 0));
            driver.UpdateLife(0, true, true);
            Assert.IsTrue(driver.UpdateLife(0, false, false), "defeat clip plays");
            int frames = 0;
            while (driver.UpdateLife(0, false, false) && frames < 1000)
            {
                driver.Render(0, Vector3.zero, Vector3.forward, false, false, Dt);
                frames++;
            }
            Assert.AreEqual(Mathf.CeilToInt(_config.DefeatSeconds / Dt), frames, 1);
        }

        [Test]
        public void CheckMatchEnd_TellsWinnersOnce()
        {
            MatchSimulation sim = TwoFighters();
            MatchState state = sim.State;
            var driver = new FighterAnimDriver(_config, SimLimits.MaxFighters);
            var s0 = new FakeSink();
            var s1 = new FakeSink();
            driver.SetSink(0, s0);
            driver.SetSink(1, s1);
            driver.CheckMatchEnd(state, 2);
            Assert.AreEqual(0, s0.Ends);

            state.Mode.Phase = Arena.Core.Modes.ModePhase.Ended;
            state.Mode.WinnerTeam = state.Fighters[1].Team;
            driver.CheckMatchEnd(state, 2);
            driver.CheckMatchEnd(state, 2);
            Assert.AreEqual(1, s0.Ends);
            Assert.IsFalse(s0.LastWon);
            Assert.IsTrue(s1.LastWon);
        }

        [Test]
        public void Render_FeedsSpeedFromPositions_TeleportIgnoredByAnimator()
        {
            var driver = new FighterAnimDriver(_config, SimLimits.MaxFighters);
            var sink = new FakeSink();
            driver.SetSink(0, sink);
            driver.Render(0, Vector3.zero, Vector3.forward, false, true, Dt);
            Assert.AreEqual(0f, sink.LastSpeed);
            driver.Render(0, new Vector3(0f, 0f, 0.05f), Vector3.forward, true, false, Dt);
            Assert.AreEqual(0.05f / Dt, sink.LastSpeed, 1e-3f);
            Assert.AreEqual(Vector3.forward, sink.LastDir);
            Assert.IsTrue(sink.LastStunned);
        }

        [Test]
        public void ProcessEventsAndRender_DoNotAllocate()
        {
            MatchSimulation sim = TwoFighters();
            var driver = new FighterAnimDriver(_config, SimLimits.MaxFighters);
            driver.SetSink(0, new FighterAnimator(_config, 0));
            driver.SetSink(1, new FighterAnimator(_config, 1));
            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.AbilityFired, 1, 0, -1, MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack), Vec2.Zero, Vec2.Up));
            events.Add(new SimEvent(SimEventType.Damaged, 1, 0, 1, 300, Vec2.Zero, Vec2.Up, MatchDefs.MakeAbilityRef(0, AbilitySlot.Attack)));
            MatchState state = sim.State;
            Frame(driver, events, state, 0);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 1; i < 120; i++) Frame(driver, events, state, i);
            });
        }

        private static void Frame(FighterAnimDriver driver, SimEventBuffer events, MatchState state, int frame)
        {
            driver.ProcessEvents(events, state);
            driver.CheckMatchEnd(state, 2);
            for (int s = 0; s < 2; s++)
            {
                driver.UpdateLife(s, true, true);
                driver.Render(s, new Vector3(0.05f * frame, 0f, 0f), Vector3.right, false, false, Dt);
            }
        }

        private static MatchSimulation TwoFighters() =>
            F.CreateWith(F.CreateDefs(), F.Slot(0, 0, 5f, 5f), F.Slot(1, 1, 8f, 9f));

        private sealed class FakeSink : IFighterAnimSink
        {
            public int Shots, Hurts, Spawns, Deaths, Ends, Resets;
            public AbilitySlot LastSlot;
            public Vector3 LastAim, LastFrom, LastDir;
            public bool LastHeavy, LastWon, LastStunned;
            public float LastSpeed;

            public void OnMove(float speed, Vector3 worldDir, float dt)
            {
                LastSpeed = speed;
                LastDir = worldDir;
            }

            public void OnShoot(AbilitySlot slot, Vector3 aimDir)
            {
                Shots++;
                LastSlot = slot;
                LastAim = aimDir;
            }

            public void OnHurt(Vector3 fromDir, bool heavy)
            {
                Hurts++;
                LastFrom = fromDir;
                LastHeavy = heavy;
            }

            public void OnSpawn() => Spawns++;
            public void OnDeath() => Deaths++;

            public void OnMatchEnd(bool won)
            {
                Ends++;
                LastWon = won;
            }

            public void SetStatus(bool stunned, bool dashing) => LastStunned = stunned;
            public void SetLook(Vector3 worldDir) { }
            public void ResetAll() => Resets++;
        }
    }
}
