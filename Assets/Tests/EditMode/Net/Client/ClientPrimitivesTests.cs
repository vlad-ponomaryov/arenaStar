using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Net;
using Arena.Net.Client;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// Building blocks of the match client (Server-Spec §2.3–2.5, S3.7a): the prediction clock (start estimate, rate by the
    /// margin error and its limit, hard resync without repeated jumps on stale margins, ticks per frame), the input history and
    /// the quantizer, the prediction-error histogram.
    /// </summary>
    public sealed class ClientPrimitivesTests
    {
        private const long TickMicros = 1000000L / 30L;

        [Test]
        public void TimeSync_Start_IsNewestTickPlusRttPlusTarget()
        {
            ClientMatchSettings s = ClientMatchSettings.CreateDefault();
            var clock = new TimeSync(s);
            clock.Start(-80, 150000L, 0L);
            Assert.AreEqual(-80 + 5 + s.TargetInputMarginTicks, clock.PredictionTick, "ceil(150 ms / 33.3 ms) = 5");
            var noRtt = new TimeSync(s);
            noRtt.Start(10, 0L, 0L);
            Assert.AreEqual(10 + 5 + s.TargetInputMarginTicks, noRtt.PredictionTick, "the fallback RTT before a sample");
        }

        [Test]
        public void TimeSync_RateFollowsTheMarginError_WithinTheLimit()
        {
            ClientMatchSettings s = ClientMatchSettings.CreateDefault();
            var clock = new TimeSync(s);
            clock.Start(0, 100000L, 0L);
            int tick = 100;
            clock.OnSnapshot(tick++, s.TargetInputMarginTicks, 100000L);
            Assert.AreEqual(1f, clock.Rate, 1e-6f, "on target");
            clock.OnSnapshot(tick++, s.TargetInputMarginTicks + 1, 100000L);
            Assert.AreEqual(1f - s.MarginGain, clock.Rate, 1e-6f, "a tick early: slower");
            clock.OnSnapshot(tick++, s.TargetInputMarginTicks - 1, 100000L);
            Assert.AreEqual(1f + s.MarginGain, clock.Rate, 1e-6f, "a tick late: faster");
            clock.OnSnapshot(tick++, s.TargetInputMarginTicks + 8, 100000L);
            Assert.AreEqual(1f - s.MaxTimeDilation, clock.Rate, 1e-6f, "never more than 5 %");
            clock.OnSnapshot(tick - 2, s.TargetInputMarginTicks, 100000L);
            Assert.AreEqual(1f - s.MaxTimeDilation, clock.Rate, 1e-6f, "a late snapshot changes nothing");
        }

        [Test]
        public void TimeSync_HardResync_JumpsOnce_UntilTheMarginsAreFromTheNewClock()
        {
            ClientMatchSettings s = ClientMatchSettings.CreateDefault();
            var clock = new TimeSync(s);
            clock.Start(0, 100000L, 0L);
            int predicted = clock.PredictionTick;
            clock.OnSnapshot(50, s.TargetInputMarginTicks - 12, 100000L);
            Assert.AreEqual(predicted + 12, clock.PredictionTick, "12 ticks late: jump 12 ahead");
            Assert.AreEqual(1, clock.Jumps);
            for (int t = 51; t < 50 + s.MarginSettleTicks; t++) clock.OnSnapshot(t, s.TargetInputMarginTicks - 12, 100000L);
            Assert.AreEqual(1, clock.Jumps, "old margins after the jump are ignored");
            Assert.AreEqual(1f, clock.Rate);
            clock.OnSnapshot(50 + s.MarginSettleTicks + 10, s.TargetInputMarginTicks + 15, 100000L);
            Assert.AreEqual(2, clock.Jumps, "a new error after the settle time jumps again");
            Assert.AreEqual(predicted + 12 - 15, clock.PredictionTick);
        }

        [Test]
        public void TimeSync_Advance_ThirtyTicksPerSecond_AtMostMaxPerFrame()
        {
            ClientMatchSettings s = ClientMatchSettings.CreateDefault();
            var clock = new TimeSync(s);
            clock.Start(0, 0L, 0L);
            int total = 0;
            long now = 0L;
            for (int i = 0; i < 600; i++)
            {
                now += 1000000L / 60L;
                total += clock.Advance(now);
            }
            Assert.AreEqual(300, total, 1, "10 s at rate 1");
            int burst = clock.Advance(now + 20 * TickMicros);
            Assert.AreEqual(s.MaxTicksPerFrame, burst, "a hitch is spread over frames");
            Assert.Greater(clock.Advance(now + 20 * TickMicros), 0, "the rest comes next frame");
        }

        [Test]
        public void InputHistory_ByTick_MissingIsZero_RingOverwrites()
        {
            var h = new InputHistory();
            var a = new PlayerInput(new Vec2(1f, 0f), Vec2.Zero, InputButtons.Attack);
            h.Set(-3, in a);
            Assert.IsTrue(h.TryGet(-3, out PlayerInput got));
            Assert.AreEqual(a, got);
            Assert.AreEqual(default(PlayerInput), h.Get(-2));
            h.Set(-3 + h.Capacity, default);
            Assert.IsFalse(h.TryGet(-3, out _), "the slot now holds a newer tick");
            h.Clear();
            Assert.IsFalse(h.TryGet(-3 + h.Capacity, out _));
        }

        [Test]
        public void InputQuantizer_IsIdempotent_AndWhatTheServerReads()
        {
            var raw = new PlayerInput(new Vec2(0.31234f, -0.7781f), new Vec2(0.1234567f, 0.98765f), InputButtons.Attack | InputButtons.AutoAim);
            PlayerInput q = InputQuantizer.Apply(in raw);
            Assert.AreEqual(q, InputQuantizer.Apply(in q), "quantizing twice changes nothing");
            Assert.AreNotEqual(raw.Move, q.Move);
            Assert.AreEqual(raw.Buttons, q.Buttons);
        }

        [Test]
        public void PredictionErrorStats_Percentiles_ExternalApart()
        {
            var e = new PredictionErrorStats(0.25f);
            for (int i = 0; i < 990; i++) e.Add(0f, false);
            for (int i = 0; i < 10; i++) e.Add(0.1f, false);
            e.Add(2f, true);
            Assert.AreEqual(1000, e.Samples);
            Assert.AreEqual(1, e.ExternalSamples);
            Assert.Less(e.Percentile(0.99), 0.01f, "99 % are zero");
            Assert.AreEqual(0.1f, e.Percentile(0.999), NetLimits.PredictionErrorBinCells);
            Assert.AreEqual(0, e.LargeCorrections, "the external sample is not a correction");
            e.Add(0.3f, false);
            Assert.AreEqual(1, e.LargeCorrections);
        }
    }
}
