using Arena.Bots;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Client;
using Arena.Net.Server.Match;
using Arena.Net.Snapshots;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Core;
using Arena.Tests.EditMode.Net.Server;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// The own fighter predicted against the match server over the virtual network (Server-Spec §11.1, S3.7a): in CI conditions
    /// (RTT 150 ms, 50 ms jitter, 2 % loss) the clock converges during the countdown, late and missing inputs stay below 1 % of
    /// ticks, the prediction error at reconciliation (without external pushes) has p99 below 0.05 cells and no correction above
    /// 0.25 cells; the clock jumps when the delay jumps; a bot-controlled flag turns prediction off and back on; reconciliation
    /// does not allocate.
    /// </summary>
    public sealed class PredictionMatchTests
    {
        private const float AcornsMatchSeconds = 175f;
        private const double MaxMissingShareCi = 0.01;
        private const float MaxP99Cells = 0.05f;

        private static void Wander(MatchClientRig rig)
        {
            for (int k = 0; k < rig.Sessions.Length; k++)
            {
                int salt = k * 7 + 3;
                rig.Scripts[k] = tick => MatchServerRig.Wander(tick, salt);
            }
        }

        private static void Report(string name, MatchClientSession s)
        {
            PredictionErrorStats e = s.Predictor.Errors;
            TestContext.WriteLine(name + ": samples " + e.Samples + " (external " + e.ExternalSamples + ", non-zero " + e.NonZeroSamples
                + "), p99 " + e.Percentile(0.99).ToString("F4") + ", max " + e.MaxCells.ToString("F4") + ", > 0.25: " + e.LargeCorrections
                + "; margin at tick 0 " + s.MarginAtZero + ", clock jumps " + s.Clock.Jumps + ", reconciliations "
                + s.Predictor.Reconciliations + ", resim ticks " + s.Predictor.ResimulatedTicks + ", snapshots " + s.SnapshotsDecoded
                + " (late " + s.SnapshotsLate + ", key " + s.Keyframes + "), inputs sent " + s.InputPacketsSent);
        }

        [Test]
        public void TwoClients_CiConditions_FullAcornsMatch_PredictionWithinThresholds()
        {
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 0, 4 }, seed: 11, conditions: MatchServerRig.CiConditions());
            Wander(rig);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(rig.AllEnded, AcornsMatchSeconds), "both clients get MatchEnded");

            ClientMatchSettings cs = ClientMatchSettings.CreateDefault();
            for (int k = 0; k < rig.Sessions.Length; k++)
            {
                MatchClientSession s = rig.Sessions[k];
                Report("client " + k, s);
                Assert.IsTrue(s.HasStart);
                Assert.AreEqual(rig.ClientSlots[k], s.OwnSlot);
                Assert.Less(s.FirstServerTick, 0, "the clock started in the countdown");
                Assert.AreEqual(rig.Room.Result.EndTick, s.Ended.EndTick);
                Assert.AreEqual(rig.Room.Result.WinnerTeam, s.Ended.WinnerTeam);
                Assert.LessOrEqual(System.Math.Abs(s.MarginAtZero - cs.TargetInputMarginTicks), 2, "the clock converged by tick 0");
                PredictionErrorStats e = s.Predictor.Errors;
                Assert.Greater(e.Samples, 3000, "reconciled on most snapshots");
                Assert.Less(e.Percentile(0.99), MaxP99Cells, "p99 of the prediction error");
                Assert.AreEqual(0, e.LargeCorrections, "no correction above 0.25 cells without external pushes");
                Assert.AreEqual(0, s.MalformedSnapshots);
                Assert.AreEqual(0, rig.Clients[k].MalformedMessages);
            }
            ServerMetrics m = rig.Host.Metrics;
            TestContext.WriteLine("server: missing input share " + m.MissingInputShare.ToString("P2") + ", late " + m.LateInputs
                + ", carried presses " + m.PressesCarried + ", takeovers " + m.Takeovers);
            Assert.Less(m.MissingInputShare, MaxMissingShareCi, "late and missing inputs < 1 % of ticks");
            Assert.AreEqual(0, rig.Room.Takeover.TakeoverCount, "nobody was taken over");
        }

        [Test]
        public void OneClient_LastBeast_CiConditions_PredictionWithinThresholds()
        {
            var rig = new MatchClientRig(ModeKind.LastBeast, new[] { 3 }, seed: 5, conditions: MatchServerRig.CiConditions());
            Wander(rig);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Tick > 1500 || rig.AllEnded(), 80f));
            MatchClientSession s = rig.Sessions[0];
            Report("last beast", s);
            PredictionErrorStats e = s.Predictor.Errors;
            Assert.Greater(e.Samples, 300);
            Assert.Less(e.Percentile(0.99), MaxP99Cells);
            Assert.AreEqual(0, e.LargeCorrections);
            Assert.Less(rig.Host.Metrics.MissingInputShare, MaxMissingShareCi);
        }

        [Test]
        public void Clock_JumpsWhenTheDelayJumps_AndSettles()
        {
            ClientMatchSettings settings = ClientMatchSettings.CreateDefault();
            settings.MaxResimTicks = 30; // the delay step below goes beyond the re-simulation limit of §13: test the clock alone
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 2 }, seed: 13, conditions: MatchServerRig.CiConditions(),
                clientSettings: settings);
            Wander(rig);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Tick > 300, 30f));
            MatchClientSession s = rig.Sessions[0];
            long jumps = s.Clock.Jumps;

            NetworkConditions slow = MatchServerRig.CiConditions();
            slow.LatencyMs += 400f;
            rig.Sockets[0].Conditions = slow; // uplink +400 ms: inputs arrive 12 ticks later
            rig.RunSeconds(6f);
            Assert.Greater(s.Clock.Jumps, jumps, "the prediction tick jumped ahead");
            AssertSettled(rig, s, "after the delay grew");

            jumps = s.Clock.Jumps;
            rig.Sockets[0].Conditions = MatchServerRig.CiConditions(); // and back: inputs now 12 ticks early
            rig.RunSeconds(6f);
            Assert.Greater(s.Clock.Jumps, jumps, "the prediction tick jumped back");
            AssertSettled(rig, s, "after the delay dropped");
        }

        private static void AssertSettled(MatchClientRig rig, MatchClientSession s, string when)
        {
            ServerMetrics m = rig.Host.Metrics;
            long applied = m.AppliedInputTicks, missing = m.MissingInputTicks;
            rig.RunSeconds(3f);
            long ticks = m.AppliedInputTicks - applied + m.MissingInputTicks - missing;
            Assert.Greater(ticks, 60);
            Assert.Less((m.MissingInputTicks - missing) / (double)ticks, 0.02, "no late inputs once settled " + when);
            int margin = s.Latest.Header.InputMarginMin;
            Assert.LessOrEqual(System.Math.Abs(margin - s.Settings.TargetInputMarginTicks), 3, "margin back near the target " + when);
        }

        [Test]
        public void BotControlledFlag_TurnsPredictionOff_AndBackOn()
        {
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 1 }, seed: 17, conditions: MatchServerRig.CiConditions());
            Wander(rig);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Tick > 200, 30f));
            MatchClientSession s = rig.Sessions[0];
            Assert.IsFalse(s.Predictor.BotControlled);

            s.Silent = true;
            Assert.IsTrue(rig.RunUntil(() => s.Predictor.BotControlled, 5f), "the server's bot takes over and says so");
            rig.RunSeconds(0.5f);
            Assert.IsTrue(s.Predictor.BotControlled);
            Assert.AreEqual(s.Latest.Own.Position, s.Predictor.Own.Position, "the own fighter is the server's, not predicted");
            Assert.AreEqual(s.Predictor.SnapshotTick < 0 ? 0 : s.Predictor.SnapshotTick, s.Predictor.ShadowTick, "nothing re-simulated");

            s.Silent = false;
            Assert.IsTrue(rig.RunUntil(() => !s.Predictor.BotControlled, 3f), "input again: the player controls the fighter");
            rig.RunSeconds(0.5f);
            Assert.Greater(s.Predictor.ShadowTick, s.Predictor.SnapshotTick, "predicting ahead of the server again");
        }

        [Test]
        public void ClientFrames_DoNotAllocate_InSteadyState()
        {
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 0, 3 }, seed: 19, conditions: MatchServerRig.CiConditions());
            Wander(rig);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 300, 30f));
            AllocationAssert.InitializeTypes(typeof(MatchSimulation).Assembly, typeof(BotController).Assembly, typeof(MatchHost).Assembly,
                typeof(SnapshotFrame).Assembly);
            rig.RunSeconds(1f);
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 120; i++) rig.Step();
            }, "host + client frames: receive, reconcile, predict, send");
            Assert.Greater(rig.Sessions[0].Predictor.Reconciliations, 300);
        }
    }
}
