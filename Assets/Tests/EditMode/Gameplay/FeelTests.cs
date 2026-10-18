using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using Arena.Gameplay.Feel;
using Arena.Gameplay.View;
using NUnit.Framework;
using UnityEngine;
using A = Arena.Tests.EditMode.Core.AbilityTestFactory;
using F = Arena.Tests.EditMode.Core.TestMatchFactory;

namespace Arena.Tests.EditMode.Gameplay
{
    public sealed class FeelTests
    {
        private const float Now = 10f;

        private static FeelConfig Feel()
        {
            var f = ScriptableObject.CreateInstance<FeelConfig>();
            f.FlashSeconds = 0.1f;
            f.HitStopSeconds = 0.04f;
            f.ShakeAmplitude = 0.25f;
            f.ShakeSeconds = 0.25f;
            f.ShakeFrequency = 28f;
            f.HitVibrationMs = 20;
            f.UltVibrationMs = 60;
            f.VibrationMinInterval = 0.08f;
            return f;
        }

        private sealed class Rig
        {
            public readonly HitStop HitStop = new HitStop();
            public readonly CameraShake Shake = new CameraShake();
            public Haptics Haptics;
            public HitFeedback Feedback;
        }

        private static Rig CreateRig(FeelConfig feel, int localSlot, int localTeam)
        {
            var rig = new Rig { Haptics = new Haptics(1f, feel.VibrationMinInterval) };
            rig.Feedback = new HitFeedback(feel, localSlot, localTeam, rig.HitStop, rig.Shake, rig.Haptics, null, null);
            return rig;
        }

        /// <summary>One tick with <paramref name="input"/> for slot 0, its events fed to the feedback; then the input is cleared.</summary>
        private static void TickWith(MatchSimulation sim, PlayerInput[] inputs, PlayerInput input, HitFeedback feedback)
        {
            inputs[0] = input;
            sim.Tick(inputs);
            feedback.Process(sim.Events, sim.State, Now);
            inputs[0] = default;
        }

        /// <summary>Ticks the simulation, feeding every tick's events to the feedback, until a hit happened.</summary>
        private static void RunUntilHit(MatchSimulation sim, PlayerInput[] inputs, HitFeedback feedback, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                sim.Tick(inputs);
                feedback.Process(sim.Events, sim.State, Now);
                if (sim.Events.CountOf(SimEventType.Damaged) > 0) return;
            }
            Assert.Fail("no hit within " + maxTicks + " ticks");
        }

        [Test]
        public void AttackHit_OfTheLocalFighter_FlashesAndVibrates_WithoutHitStop()
        {
            FeelConfig feel = Feel();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var inputs = F.Inputs();
            Rig rig = CreateRig(feel, 0, 0);
            TickWith(sim, inputs, new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Attack), rig.Feedback);
            RunUntilHit(sim, inputs, rig.Feedback, 30);

            Assert.AreEqual(1, rig.Feedback.HitsShown);
            Assert.IsTrue(rig.Feedback.IsFlashing(1, Now));
            Assert.IsFalse(rig.Feedback.IsFlashing(1, Now + feel.FlashSeconds + 0.01f), "the flash is short");
            Assert.IsFalse(rig.Feedback.IsFlashing(0, Now), "the shooter does not flash");
            Assert.AreEqual(1, rig.Haptics.PlayedCount, "light vibration per hit");
            Assert.IsFalse(rig.HitStop.IsActive, "no hit stop for an attack");
            Assert.IsFalse(rig.Shake.IsActive);
            Assert.AreEqual(0, rig.Feedback.UltHitsFelt);
        }

        [Test]
        public void UltHit_OfTheLocalFighter_StopsViews_ShakesCamera_AndPlaysUltSound()
        {
            FeelConfig feel = Feel();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var inputs = F.Inputs();
            Rig rig = CreateRig(feel, 0, 0);
            A.ChargeUlt(sim, 0);
            TickWith(sim, inputs, new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Ult), rig.Feedback);
            Assert.AreEqual(1, rig.Feedback.UltSoundsPlayed, "the ult sound plays when the ult is fired");
            RunUntilHit(sim, inputs, rig.Feedback, 30);

            Assert.Greater(rig.Feedback.UltHitsFelt, 0);
            Assert.IsTrue(rig.HitStop.IsActive, "hit stop on an ult hit");
            Assert.IsTrue(rig.Shake.IsActive, "camera shake on an ult hit");
            rig.HitStop.Update(0.039f);
            Assert.IsTrue(rig.HitStop.IsActive, "40 ms");
            rig.HitStop.Update(0.002f);
            Assert.IsFalse(rig.HitStop.IsActive);
        }

        [Test]
        public void HitBetweenOthers_IsShown_ButDoesNotVibrateOrStop()
        {
            FeelConfig feel = Feel();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f), A.Slot(A.Dummy, 0, 3f, 3f));
            var inputs = F.Inputs();
            Rig rig = CreateRig(feel, 2, 0);
            A.ChargeUlt(sim, 0);
            TickWith(sim, inputs, new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Ult), rig.Feedback);
            RunUntilHit(sim, inputs, rig.Feedback, 30);

            Assert.Greater(rig.Feedback.HitsShown, 0, "a seen hit shows a number and a flash");
            Assert.AreEqual(0, rig.Haptics.PlayedCount, "not our hit: no vibration");
            Assert.IsFalse(rig.HitStop.IsActive, "not our hit: no hit stop");
        }

        [Test]
        public void ProcessingHits_DoesNotAllocate()
        {
            FeelConfig feel = Feel();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            var inputs = F.Inputs();
            Rig rig = CreateRig(feel, 0, 0);
            TickWith(sim, inputs, new PlayerInput(Vec2.Zero, Vec2.Up, InputButtons.Attack), rig.Feedback);
            RunUntilHit(sim, inputs, rig.Feedback, 30);
            SimEventBuffer events = sim.Events;
            MatchState state = sim.State;
            HitFeedback feedback = rig.Feedback;
            Arena.Tests.EditMode.Core.AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 100; i++) feedback.Process(events, state, Now + i);
            });
        }

        [Test]
        public void UltReady_OfTheLocalFighter_GivesOneSignal()
        {
            FeelConfig feel = Feel();
            var sim = A.Create(A.Slot(A.Pix, 0, 10f, 10f), A.Slot(A.Dummy, 1, 10f, 13f));
            Rig rig = CreateRig(feel, 0, 0);
            var events = new SimEventBuffer();
            events.Add(new SimEvent(SimEventType.UltReady, 1, 0, -1, A.PixUltCharge, Vec2.Zero, Vec2.Zero));
            events.Add(new SimEvent(SimEventType.UltReady, 1, 1, -1, A.PixUltCharge, Vec2.Zero, Vec2.Zero));
            rig.Feedback.Process(events, sim.State, Now);
            Assert.AreEqual(1, rig.Feedback.UltReadySignals, "only the local fighter's ult");
        }

        [Test]
        public void Haptics_RespectsStrengthAndInterval()
        {
            var off = new Haptics(0f, 0.08f);
            off.Vibrate(20, 1f);
            Assert.AreEqual(0, off.PlayedCount, "strength 0 = off");

            var h = new Haptics(0.5f, 0.08f);
            h.Vibrate(20, 1f);
            h.Vibrate(20, 1.05f);
            h.Vibrate(20, 1.09f);
            Assert.AreEqual(2, h.PlayedCount, "at most one per interval");
        }

        [Test]
        public void CameraShake_FadesOut()
        {
            var shake = new CameraShake();
            shake.Trigger(0.25f, 0.25f, 28f);
            shake.Update(0.01f);
            Assert.Greater(shake.Offset.magnitude, 0f);
            Assert.LessOrEqual(shake.Offset.magnitude, 0.25f * Mathf.Sqrt(2f));
            Assert.AreEqual(0f, shake.Offset.y);
            shake.Update(0.3f);
            Assert.IsFalse(shake.IsActive);
            Assert.AreEqual(Vector3.zero, shake.Offset);
        }

        [Test]
        public void TelegraphProgress_ShowsTheLastHalfSecond()
        {
            int telegraph = SimTime.SecondsToTicks(0.5f);
            Assert.AreEqual(15, telegraph);
            var a = new AreaState { Active = true, ActivateTick = 100, Radius = 1.5f };

            Assert.AreEqual(-1f, TelegraphViewPool.Progress(in a, 80, telegraph, 0f), "too early");
            Assert.AreEqual(0f, TelegraphViewPool.Progress(in a, 85, telegraph, 0f), 1e-5f, "appears 0.5 s before");
            Assert.AreEqual(0.5f, TelegraphViewPool.Progress(in a, 92, telegraph, 0.5f), 1e-5f);
            Assert.AreEqual(1f, TelegraphViewPool.Progress(in a, 100, telegraph, 0f), 1e-5f);
            a.Triggered = true;
            Assert.AreEqual(-1f, TelegraphViewPool.Progress(in a, 100, telegraph, 0f), "gone once it triggered");
        }

        [Test]
        public void ProceduralSound_HasTheLength_AndStaysInRange()
        {
            var s = new ProceduralSoundSettings(700f, 300f, 0f, 0.07f, 0.45f, 0.6f);
            float[] samples = ProceduralSounds.Generate(s, 22050);
            Assert.AreEqual(Mathf.RoundToInt(0.07f * 22050), samples.Length);
            float peak = 0f;
            foreach (float v in samples) peak = Mathf.Max(peak, Mathf.Abs(v));
            Assert.Greater(peak, 0.05f, "audible");
            Assert.LessOrEqual(peak, 0.45f + 1e-4f, "never above the volume");
            Assert.AreEqual(0f, samples[0], 1e-6f, "starts from silence (no click)");
        }
    }
}
