using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Net.Client;
using Arena.Net.Snapshots;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>Building blocks of the client display (S3.7b): render clock, display slots, event schedule.</summary>
    public sealed class PresentationPrimitivesTests
    {
        private const long TickMicros = 1000000L / 30L;

        private static void Arrive(SnapshotFrameRing ring, InterpolationBuffer interp, int tick, long micros)
        {
            SnapshotFrame f = ring.Acquire(tick);
            f.Valid = true;
            interp.OnSnapshot(tick, micros);
        }

        [Test]
        public void RenderClock_SettlesAtTheDelayBehindTheNewest_WithoutJitter()
        {
            ClientMatchSettings settings = ClientMatchSettings.CreateDefault();
            var ring = new SnapshotFrameRing();
            var interp = new InterpolationBuffer(settings, ring);
            for (int t = 0; t < 600; t++)
            {
                Arrive(ring, interp, t, t * TickMicros);
                interp.Advance();
                Assert.IsTrue(interp.Sample());
            }
            Assert.AreEqual(settings.InterpDelaySnapshots, interp.DelayTicks, 0.01f);
            Assert.AreEqual(interp.NewestTick - interp.DelayTicks, interp.RenderTick, 1.05);
            Assert.IsFalse(interp.Extrapolating);
            Assert.IsNotNull(interp.To);
            Assert.LessOrEqual(interp.From.Tick, interp.RenderTick);
            Assert.Greater(interp.To.Tick, interp.RenderTick);
        }

        [Test]
        public void RenderClock_WithJitter_DelayGrows_ButStaysUnderTheCap()
        {
            ClientMatchSettings settings = ClientMatchSettings.CreateDefault();
            var ring = new SnapshotFrameRing();
            var interp = new InterpolationBuffer(settings, ring);
            for (int t = 0; t < 300; t++)
                Arrive(ring, interp, t, t * TickMicros + ((t & 1) == 0 ? 0 : 25000)); // ±25 ms
            Assert.Greater(interp.DelayTicks, settings.InterpDelaySnapshots + 1f);
            Assert.LessOrEqual(interp.DelayTicks, settings.InterpDelayMaxMs * 30f / 1000f + 1e-3f);
        }

        [Test]
        public void RenderClock_WithoutNewData_Extrapolates_UpToTheCap()
        {
            ClientMatchSettings settings = ClientMatchSettings.CreateDefault();
            var ring = new SnapshotFrameRing();
            var interp = new InterpolationBuffer(settings, ring);
            for (int t = 0; t < 100; t++)
            {
                Arrive(ring, interp, t, t * TickMicros);
                interp.Advance();
            }
            for (int i = 0; i < 8; i++) interp.Advance(); // snapshots stop
            Assert.IsTrue(interp.Sample());
            Assert.IsTrue(interp.Extrapolating);
            Assert.IsNull(interp.To);
            Assert.AreEqual(99, interp.From.Tick);
            Assert.LessOrEqual(interp.ExtrapolationTicks, settings.MaxExtrapolationMs * 30f / 1000f + 1e-3f);
        }

        /// <summary>
        /// A stall of the snapshots (Wi-Fi pause, F2): the render clock holds at the extrapolation cap instead of running past the
        /// data and hard-jumping back every few ticks (the world would replay the last 0.3 s).
        /// </summary>
        [Test]
        public void RenderClock_SnapshotsStop_NeverRunsBack_HoldsAtTheExtrapolationCap()
        {
            ClientMatchSettings settings = ClientMatchSettings.CreateDefault();
            var ring = new SnapshotFrameRing();
            var interp = new InterpolationBuffer(settings, ring);
            for (int t = 0; t < 100; t++)
            {
                Arrive(ring, interp, t, t * TickMicros);
                interp.Advance();
            }
            long jumps = interp.Jumps;
            double previous = interp.RenderTick;
            double cap = 99 + settings.MaxExtrapolationMs * 30.0 / 1000.0;
            for (int i = 0; i < 60; i++)
            {
                interp.Advance();
                Assert.GreaterOrEqual(interp.RenderTick, previous, $"advance {i}: the render clock went back");
                Assert.LessOrEqual(interp.RenderTick, cap + 1e-6, "no further than the extrapolation cap");
                previous = interp.RenderTick;
            }
            Assert.AreEqual(jumps, interp.Jumps, "no hard jumps without new data");

            // The data returns: the clock catches up forward, it is not thrown back.
            for (int t = 100; t < 160; t++)
            {
                Arrive(ring, interp, t, (t + 60) * TickMicros);
                interp.Advance();
                Assert.GreaterOrEqual(interp.RenderTick, previous, $"tick {t}: back after the stall");
                previous = interp.RenderTick;
            }
        }

        [Test]
        public void DisplaySlots_StableByKey_OrdinalsForEqualKeys_FreedWhenGone()
        {
            var table = new DisplaySlotTable(8);
            long a = DisplaySlotTable.MakeKey(1, 100, 4);
            long b = DisplaySlotTable.MakeKey(2, 100, 4);
            table.Begin();
            int a0 = table.Acquire(a, 5, 3, out int sa0);
            int a1 = table.Acquire(a, 5, 3, out _); // a second pellet of the same volley
            int b0 = table.Acquire(b, 0, 7, out _);
            table.End();
            Assert.AreEqual(5, a0, "the preferred slot when free");
            Assert.AreNotEqual(a0, a1);
            Assert.AreEqual(3, sa0);

            table.Begin();
            int b0Again = table.Acquire(b, 3, 9, out int sb);
            int a0Again = table.Acquire(a, 1, 9, out int sa);
            Assert.AreEqual(a1, table.Acquire(a, 1, 9, out _));
            table.End();
            Assert.AreEqual(b0, b0Again, "same key, same slot, whatever the order");
            Assert.AreEqual(a0, a0Again);
            Assert.AreEqual(7, sb, "the shift of the first sight is kept");
            Assert.AreEqual(3, sa);

            table.Begin();
            int c = table.Acquire(DisplaySlotTable.MakeKey(3, 200, 1), a1, 0, out _);
            table.End();
            Assert.AreNotEqual(a1, c, "a slot is not reused in the build that frees it");
            table.Begin();
            Assert.AreEqual(a1, table.Acquire(DisplaySlotTable.MakeKey(4, 300, 1), a1, 0, out _), "freed afterwards");
            table.End();
        }

        private static NetSnapshotEvent Damage(int tick, int source, int target, int value) =>
            new NetSnapshotEvent { Type = SimEventType.Damaged, Tick = tick, Source = source, Target = target, Value = value, AbilityRef = -1 };

        [Test]
        public void Events_RepeatsDropped_EqualCopiesKept_ReleasedByRenderTick_OwnAtOnce()
        {
            var scheduler = new EventScheduler();
            var shown = new MatchState();
            var output = new SimEventBuffer();
            var batch = new NetSnapshotEvent[8];
            batch[0] = Damage(10, 2, 3, 100);
            batch[1] = Damage(10, 2, 3, 100); // a second pellet: equal, but real
            batch[2] = Damage(10, 0, 4, 50); // own source (slot 0)
            batch[3] = new NetSnapshotEvent { Type = SimEventType.AbilityFired, Tick = 10, Source = 0, Target = -1, AbilityRef = 2 };
            scheduler.Add(batch, 4, 0, true);
            scheduler.Add(batch, 4, 0, true); // the next snapshot repeats the window
            Assert.AreEqual(4, scheduler.Received);
            Assert.AreEqual(4, scheduler.Duplicates);
            Assert.AreEqual(3, scheduler.PendingCount, "the own predicted AbilityFired is not replayed");

            scheduler.Release(9, 5, 20, shown, 0, output);
            Assert.AreEqual(1, output.Count, "own hit at once");
            Assert.AreEqual(0, output[0].Source);
            Assert.AreEqual(20, output[0].Tick);

            output.Clear();
            scheduler.Release(10, 5, 21, shown, 0, output);
            Assert.AreEqual(0, output.Count, "an event of tick 10 is shown with the frame of tick 11");
            scheduler.Release(11, 5, 22, shown, 0, output);
            Assert.AreEqual(2, output.Count);
            Assert.AreEqual(15, output[0].Tick, "event tick + shift");
            Assert.AreEqual(0, scheduler.PendingCount);
        }
    }
}
