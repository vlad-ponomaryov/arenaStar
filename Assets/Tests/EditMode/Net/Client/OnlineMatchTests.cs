using Arena.Bots;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Server.Match;
using Arena.Net.Snapshots;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Core;
using Arena.Tests.EditMode.Net.Server;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// Full online matches in tests (Server-Spec §11.1, S3.7b): the match server and 2 or 6 bot clients — each bot reads only its
    /// client's display state — over the virtual network. CI conditions (RTT 150 ms, 50 ms jitter, 2 % loss): prediction error
    /// p99 &lt; 0.05 cells and no correction above 0.25, no teleports, extrapolation &lt; 2 % of display ticks with p99 run &lt; 100 ms,
    /// late and missing inputs &lt; 1 %, traffic ≤ 10 KB/s down and ≤ 4 KB/s up, the same result at every client as on the server.
    /// "Bad" conditions (RTT 250 ms, 5 % loss in bursts): the match ends with the same result, p99 &lt; 0.25 cells, no teleport over
    /// 1 cell. Plus: the display timeline of a lob (foreign telegraph 0.5 s before the burst on screen, own at once) and no
    /// allocations in the client frames with bots.
    /// </summary>
    public sealed class OnlineMatchTests
    {
        private const float AcornsMatchSeconds = 180f;
        private const double MaxMissingShareCi = 0.01;
        private const float MaxP99CellsCi = 0.05f;
        private const float MaxP99CellsBad = 0.25f;
        private const double MaxExtrapolationShareCi = 0.02;
        private const float MaxExtrapolationP99MsCi = 100f;
        private const double MaxDownBytesPerSecond = 10 * 1024;
        private const double MaxUpBytesPerSecond = 4 * 1024;

        /// <summary>RTT 250 ms, 50 ms jitter, ≈ 5 % loss partly in bursts (Gilbert–Elliott) — split over both directions.</summary>
        public static NetworkConditions BadConditions() => new NetworkConditions
        {
            LatencyMs = 125f,
            JitterMs = 25f,
            LossPercent = 1f,
            BurstEnterPercent = 0.5f,
            BurstExitPercent = 25f,
            BurstLossPercent = 75f
        };

        private static int[] Slots(int n)
        {
            var slots = new int[n];
            for (int i = 0; i < n; i++) slots[i] = n == 2 ? i * 3 : i; // 2 clients: one per team
            return slots;
        }

        private static MatchClientRig RunBotMatch(int clients, NetworkConditions conditions, int seed, out double seconds)
        {
            var rig = new MatchClientRig(ModeKind.Acorns, Slots(clients), seed: seed, conditions: conditions);
            rig.UseBots(BotDifficulty.Easy, seed);
            rig.ConnectAll();
            long start = rig.Now;
            Assert.IsTrue(rig.RunUntil(rig.AllEnded, AcornsMatchSeconds), "every client gets MatchEnded");
            rig.RunSeconds(0.2f); // the result reaches the display state on the next frame
            seconds = (rig.Now - start) / 1e6;
            return rig;
        }

        private static void Report(string name, MatchClientSession s, double seconds)
        {
            PredictionErrorStats e = s.Predictor.Errors;
            ClientNetStats n = s.NetStats;
            NetConnection c = s.Client.Transport.Connection;
            TestContext.WriteLine(name + ": prediction samples " + e.Samples + " (external " + e.ExternalSamples + "), p99 "
                + e.Percentile(0.99).ToString("F4") + ", max " + e.MaxCells.ToString("F3") + ", > 0.25: " + e.LargeCorrections
                + "; display ticks " + n.PresentedTicks + ", extrapolated " + n.ExtrapolationShare.ToString("P2") + " (runs "
                + n.ExtrapolationRuns + ", p99 " + n.ExtrapolationPercentileMs(0.99).ToString("F0") + " ms, max "
                + n.MaxExtrapolationMs.ToString("F0") + " ms), delay " + n.MeanDelayTicks.ToString("F2") + " ticks, render jumps "
                + s.Interpolation.Jumps + "; motion samples " + n.MotionSamples + ", teleports " + n.Teleports + " (large "
                + n.LargeTeleports + ", max excess " + n.MaxTeleportExcess.ToString("F3") + ", last: slot " + n.LastTeleportSlot + " tick " + n.LastTeleportTick + " " + n.LastTeleportCells.ToString("F2") + " cells, own " + s.OwnSlot + "); events " + s.Scheduler.Released
                + " (dup " + s.Scheduler.Duplicates + ", dropped " + s.Scheduler.Dropped + "); down "
                + (c.BytesReceived / seconds / 1024).ToString("F2") + " KB/s, up " + (c.BytesSent / seconds / 1024).ToString("F2")
                + " KB/s; clock jumps " + s.Clock.Jumps);
        }

        private static void AssertResult(MatchClientRig rig, MatchClientSession s)
        {
            MatchEndedMessage server = rig.Room.Result;
            Assert.AreEqual(server.EndTick, s.Ended.EndTick, "end tick");
            Assert.AreEqual(server.WinnerTeam, s.Ended.WinnerTeam, "winner");
            Assert.AreEqual(server.MvpSlot, s.Ended.MvpSlot, "MVP");
            MatchState shown = s.Presentation.State;
            Assert.IsTrue(shown.Mode.IsEnded, "the display state shows the end");
            Assert.AreEqual(server.WinnerTeam, shown.Mode.WinnerTeam);
            for (int i = 0; i < server.SlotCount; i++)
            {
                Assert.AreEqual(server.Slots[i].Kills, shown.Stats[i].Kills, "kills of slot " + i);
                Assert.AreEqual(server.Slots[i].DamageDealt, shown.Stats[i].DamageDealt, "damage of slot " + i);
            }
            Assert.AreEqual(0, s.MalformedSnapshots);
            Assert.AreEqual(0, s.Client.MalformedMessages);
        }

        private static void AssertCi(MatchClientRig rig, double seconds)
        {
            for (int k = 0; k < rig.Sessions.Length; k++)
            {
                MatchClientSession s = rig.Sessions[k];
                Report("client " + k, s, seconds);
                AssertResult(rig, s);
                PredictionErrorStats e = s.Predictor.Errors;
                Assert.Greater(e.Samples, 2000, "reconciled on most snapshots");
                Assert.Less(e.Percentile(0.99), MaxP99CellsCi, "p99 of the prediction error");
                Assert.AreEqual(0, e.LargeCorrections, "no correction above 0.25 cells without external pushes");
                ClientNetStats n = s.NetStats;
                Assert.Greater(n.MotionSamples, 10000);
                Assert.AreEqual(0, n.Teleports, "no teleports");
                Assert.Less(n.ExtrapolationShare, MaxExtrapolationShareCi, "extrapolated display ticks");
                Assert.Less(n.ExtrapolationPercentileMs(0.99), MaxExtrapolationP99MsCi, "p99 of an extrapolation run");
                NetConnection c = s.Client.Transport.Connection;
                Assert.LessOrEqual(c.BytesReceived / seconds, MaxDownBytesPerSecond, "traffic down");
                Assert.LessOrEqual(c.BytesSent / seconds, MaxUpBytesPerSecond, "traffic up");
            }
            ServerMetrics m = rig.Host.Metrics;
            TestContext.WriteLine("server: missing input share " + m.MissingInputShare.ToString("P2") + ", late " + m.LateInputs
                + ", carried presses " + m.PressesCarried + ", takeovers " + m.Takeovers);
            Assert.Less(m.MissingInputShare, MaxMissingShareCi, "late and missing inputs < 1 % of ticks");
            Assert.AreEqual(0, rig.Room.Takeover.TakeoverCount, "nobody was taken over");
        }

        [Test]
        public void TwoBotClients_CiConditions_FullAcornsMatch_WithinThresholds()
        {
            MatchClientRig rig = RunBotMatch(2, MatchServerRig.CiConditions(), 21, out double seconds);
            AssertCi(rig, seconds);
        }

        [Test]
        public void SixBotClients_CiConditions_FullAcornsMatch_WithinThresholds()
        {
            MatchClientRig rig = RunBotMatch(6, MatchServerRig.CiConditions(), 23, out double seconds);
            AssertCi(rig, seconds);
        }

        [Test]
        public void SixBotClients_BadConditions_FullAcornsMatch_EndsWithSoftThresholds()
        {
            MatchClientRig rig = RunBotMatch(6, BadConditions(), 29, out double seconds);
            for (int k = 0; k < rig.Sessions.Length; k++)
            {
                MatchClientSession s = rig.Sessions[k];
                Report("client " + k, s, seconds);
                AssertResult(rig, s);
                Assert.Less(s.Predictor.Errors.Percentile(0.99), MaxP99CellsBad, "p99 of the prediction error");
                Assert.AreEqual(0, s.NetStats.LargeTeleports, "no teleport over 1 cell");
            }
            TestContext.WriteLine("server: missing input share " + rig.Host.Metrics.MissingInputShare.ToString("P2"));
        }

        [Test]
        public void Lob_ForeignTelegraphStartsHalfASecondBeforeTheBurstOnScreen_OwnAtOnce()
        {
            int lobber = FindLobFighter();
            Assert.GreaterOrEqual(lobber, 0, "a fighter with a lob attack");
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 0, 3 }, seed: 31, conditions: MatchServerRig.CiConditions(),
                fighters: new[] { lobber, 0 });
            rig.Scripts[0] = tick => tick > 0 && tick % 40 == 0 ? new PlayerInput(Vec2.Zero, Vec2.Zero, InputButtons.Attack)
                : default;
            rig.ConnectAll();
            AbilityDef lob = rig.Bundle.Fighters[lobber].Attack;
            int flight = lob.AreaDelayTicks;

            var ownFirstLeft = new System.Collections.Generic.List<int>();
            var foreignFirstLeft = new System.Collections.Generic.List<int>();
            var foreignLastLeft = new System.Collections.Generic.List<int>();
            var foreignShift = new System.Collections.Generic.List<int>();
            var ownSeen = new System.Collections.Generic.HashSet<int>();
            var foreignSeen = new System.Collections.Generic.Dictionary<int, int>(); // shifted spawn tick → last "left"
            long end = rig.Now + 20 * 1000000L;
            while (rig.Now < end)
            {
                rig.Step();
                PresentationState a = rig.Sessions[0].Presentation, b = rig.Sessions[1].Presentation;
                if (a != null && a.HasState) ObserveOwn(a.State, ownSeen, ownFirstLeft);
                if (b != null && b.HasState) ObserveForeign(b, foreignSeen, foreignFirstLeft, foreignLastLeft, foreignShift);
            }
            TestContext.WriteLine("flight " + flight + " ticks; own first left " + string.Join(",", ownFirstLeft) + "; foreign first left "
                + string.Join(",", foreignFirstLeft) + "; foreign last left " + string.Join(",", foreignLastLeft) + "; shift "
                + string.Join(",", foreignShift));
            Assert.Greater(ownFirstLeft.Count, 5, "own lobs shown");
            Assert.Greater(foreignFirstLeft.Count, 5, "foreign lobs shown");
            foreach (int left in ownFirstLeft) Assert.AreEqual(flight - 1, left, "own lob: shown at once, the whole flight ahead");
            foreach (int left in foreignFirstLeft)
                Assert.That(left, Is.InRange(flight - 2, flight), "foreign lob: the telegraph starts a flight before the burst on screen");
            foreach (int left in foreignLastLeft) Assert.That(left, Is.InRange(-1, 1), "foreign lob bursts on screen at its activate tick");
            foreach (int shift in foreignShift) Assert.GreaterOrEqual(shift, 4, "the foreign lob is shown in its own (older) time");
        }

        private static int FindLobFighter()
        {
            Arena.Net.Content.ContentBundle bundle = Arena.Tests.EditMode.Net.Snapshots.SnapshotMatchRig.LoadBundle();
            for (int i = 0; i < bundle.Fighters.Length; i++)
                if (bundle.Fighters[i].Attack != null && bundle.Fighters[i].Attack.Delivery == DeliveryKind.Lob) return i;
            return -1;
        }

        private static void ObserveOwn(MatchState state, System.Collections.Generic.HashSet<int> seen,
            System.Collections.Generic.List<int> firstLeft)
        {
            for (int i = 0; i < state.Areas.Length; i++)
            {
                ref AreaState area = ref state.Areas[i];
                if (!area.Active || area.Kind != AreaKind.Burst || area.Owner != 0 || area.Triggered) continue;
                if (seen.Add(area.SpawnTick)) firstLeft.Add(area.ActivateTick - state.Globals.Tick);
            }
        }

        private static void ObserveForeign(PresentationState p, System.Collections.Generic.Dictionary<int, int> seen,
            System.Collections.Generic.List<int> firstLeft, System.Collections.Generic.List<int> lastLeft,
            System.Collections.Generic.List<int> shifts)
        {
            MatchState state = p.State;
            var now = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < state.Areas.Length; i++)
            {
                ref AreaState area = ref state.Areas[i];
                if (!area.Active || area.Kind != AreaKind.Burst || area.Owner != 0 || area.Triggered) continue;
                int left = area.ActivateTick - state.Globals.Tick;
                now.Add(area.SpawnTick);
                if (!seen.ContainsKey(area.SpawnTick))
                {
                    firstLeft.Add(left);
                    shifts.Add(p.Shift);
                }
                seen[area.SpawnTick] = left;
            }
            var gone = new System.Collections.Generic.List<int>();
            foreach (var kv in seen)
                if (!now.Contains(kv.Key) && kv.Value != int.MinValue) gone.Add(kv.Key);
            foreach (int key in gone)
            {
                lastLeft.Add(seen[key]);
                seen[key] = int.MinValue; // done
            }
        }

        [Test]
        public void BotClientFrames_DoNotAllocate_InSteadyState()
        {
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 0, 1, 3, 4 }, seed: 37, conditions: MatchServerRig.CiConditions());
            rig.UseBots(BotDifficulty.Easy, 3);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 300, 30f));
            AllocationAssert.InitializeTypes(typeof(MatchSimulation).Assembly, typeof(BotController).Assembly, typeof(MatchHost).Assembly,
                typeof(SnapshotFrame).Assembly);
            rig.RunSeconds(1f);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 120; i++) rig.Step();
            }, "host + bot client frames: receive, reconcile, interpolate, display state, bot, predict, send");
            Assert.Greater(rig.Sessions[0].NetStats.PresentedTicks, 300);
        }
    }
}
