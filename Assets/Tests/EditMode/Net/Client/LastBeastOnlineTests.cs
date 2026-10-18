using Arena.Bots;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Match;
using Arena.Net.Snapshots;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Net.Server;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// «Last Beast» online with 10 bot clients (Server-Spec §11.1, S3.14): a full free-for-all match over the virtual network in CI
    /// conditions (RTT 150 ms, 50 ms jitter, 2 % loss), every client played by a <see cref="ClientBot"/> that reads only its display
    /// state. Thresholds of §11.1 (prediction error, corrections, teleports, extrapolation, late inputs, traffic down/up), the same
    /// result as on the server, every fighter that is out before the end tick gets <c>Eliminated</c> with its place, and the area-of-interest property:
    /// no enemy or projectile beyond <c>AoiRadiusCellsLastBeast</c> is ever in a client's snapshot, while there were far ones to cut.
    /// </summary>
    public sealed class LastBeastOnlineTests
    {
        private const int Clients = SimLimits.MaxFighters;
        private const float MatchSeconds = 400f;
        private const float MaxP99Cells = 0.05f;
        private const double MaxMissingShare = 0.01;
        private const double MaxExtrapolationShare = 0.02;
        private const float MaxExtrapolationP99Ms = 100f;
        private const double MaxDownBytesPerSecond = 10 * 1024;
        private const double MaxUpBytesPerSecond = 4 * 1024;

        /// <summary>Quantisation of a snapshot position (1/1024 cell) and float rounding.</summary>
        private const float AoiTolerance = 0.01f;

        /// <summary>Movement of both ends during the snapshot's way (RTT/2 + interval + jitter) — a server-side "far" must be farther.</summary>
        private const float FarFighterMargin = 3f;
        private const float FarProjectileMargin = 6f;

        [Test]
        public void TenBotClients_CiConditions_FullLastBeastMatch_WithinThresholds_AoiAndEliminated()
        {
            var slots = new int[Clients];
            for (int i = 0; i < Clients; i++) slots[i] = i;
            var rig = new MatchClientRig(ModeKind.LastBeast, slots, seed: 41, conditions: MatchServerRig.CiConditions());
            Assert.AreEqual(Clients, rig.Room.SlotCount, "10 players in the room");
            rig.UseBots(BotDifficulty.Easy, 41);
            rig.ConnectAll();
            float aoi = rig.Room.Builder.Settings.AoiRadiusFor(ModeKind.LastBeast);
            Assert.Greater(aoi, 0f, "AOI is on in Last Beast");

            long start = rig.Now;
            long end = start + (long)(MatchSeconds * 1e6);
            int farFighters = 0, farProjectiles = 0, checkedViews = 0;
            while (rig.Now < end && !rig.AllEnded())
            {
                rig.Step();
                if (rig.Room.Phase != MatchPhase.Running) continue;
                MatchState server = rig.Room.Simulation.State;
                for (int k = 0; k < Clients; k++)
                {
                    MatchClientSession s = rig.Sessions[k];
                    SnapshotFrame view = s.Latest;
                    if (view == null || !view.Valid || !view.Own.Alive) continue;
                    checkedViews++;
                    CheckAoi(view, server, s.OwnSlot, aoi, ref farFighters, ref farProjectiles);
                }
            }
            Assert.IsTrue(rig.AllEnded(), "every client gets MatchEnded");
            rig.RunSeconds(0.2f);
            double seconds = (rig.Now - start) / 1e6;

            MatchEndedMessage result = rig.Room.Result;
            int winners = 0;
            for (int k = 0; k < Clients; k++)
            {
                MatchClientSession s = rig.Sessions[k];
                int slot = s.OwnSlot;
                int place = result.Slots[slot].Place;
                Assert.That(place, Is.InRange(1, Clients), "slot " + slot + " has a place");
                if (place == 1) winners++;
                else if (s.HasEliminated || place > 2) // the runner-up is out on the end tick: MatchEnded is its result
                {
                    Assert.IsTrue(s.HasEliminated, "slot " + slot + " (place " + place + ") got Eliminated");
                    Assert.AreEqual(slot, s.Eliminated.Slot);
                    Assert.AreEqual(place, s.Eliminated.Result.Place, "the place in Eliminated");
                }
                Assert.AreEqual(result.EndTick, s.Ended.EndTick, "end tick");
                Assert.AreEqual(result.WinnerTeam, s.Ended.WinnerTeam, "winner");
                Assert.AreEqual(result.WinnerTeam, s.Presentation.State.Mode.WinnerTeam, "the display state shows the winner");
                Assert.AreEqual(0, s.MalformedSnapshots);
                Assert.AreEqual(0, s.Client.MalformedMessages);

                PredictionErrorStats e = s.Predictor.Errors;
                ClientNetStats n = s.NetStats;
                NetConnection c = s.Client.Transport.Connection;
                TestContext.WriteLine("client " + k + " (place " + place + "): prediction samples " + e.Samples + ", p99 "
                    + e.Percentile(0.99).ToString("F4") + ", > 0.25: " + e.LargeCorrections + "; display ticks " + n.PresentedTicks
                    + ", extrapolated " + n.ExtrapolationShare.ToString("P2") + " (p99 " + n.ExtrapolationPercentileMs(0.99).ToString("F0")
                    + " ms), teleports " + n.Teleports + "; down " + (c.BytesReceived / seconds / 1024).ToString("F2") + " KB/s, up "
                    + (c.BytesSent / seconds / 1024).ToString("F2") + " KB/s");
                Assert.Greater(e.Samples, 100, "reconciled while alive");
                Assert.Less(e.Percentile(0.99), MaxP99Cells, "p99 of the prediction error");
                Assert.AreEqual(0, e.LargeCorrections, "no correction above 0.25 cells without external pushes");
                Assert.AreEqual(0, n.Teleports, "no teleports");
                Assert.Less(n.ExtrapolationShare, MaxExtrapolationShare, "extrapolated display ticks");
                Assert.Less(n.ExtrapolationPercentileMs(0.99), MaxExtrapolationP99Ms, "p99 of an extrapolation run");
                Assert.LessOrEqual(c.BytesReceived / seconds, MaxDownBytesPerSecond, "traffic down");
                Assert.LessOrEqual(c.BytesSent / seconds, MaxUpBytesPerSecond, "traffic up");
            }
            Assert.AreEqual(1, winners, "one winner");

            ServerMetrics m = rig.Host.Metrics;
            string summary = "Last Beast online, 10 bot clients: " + seconds.ToString("F1") + " s, end tick " + result.EndTick + ", winner "
                + result.WinnerTeam + ", missing input share " + m.MissingInputShare.ToString("P2") + ", views checked " + checkedViews
                + ", far fighters cut " + farFighters + ", far projectiles cut " + farProjectiles;
            UnityEngine.Debug.Log(summary);
            TestContext.WriteLine(summary);
            Assert.Less(m.MissingInputShare, MaxMissingShare, "late and missing inputs < 1 % of ticks");
            Assert.AreEqual(0, rig.Room.Takeover.TakeoverCount, "nobody was taken over");
            Assert.Greater(farFighters, 0, "there were far fighters to leave out");
        }

        /// <summary>
        /// Nothing in the view beyond the AOI of the own position at the snapshot's tick; a fighter / projectile that is far
        /// (with a margin for the snapshot's way) on the server now is not in the view.
        /// </summary>
        private static void CheckAoi(SnapshotFrame view, MatchState server, int ownSlot, float aoi, ref int farFighters, ref int farProjectiles)
        {
            Vec2 eye = view.Own.Position;
            int team = view.Own.Team;
            for (int i = 0; i < view.Fighters.Count; i++)
            {
                ref NetFighterView v = ref view.Fighters.ItemAt(i);
                var p = new Vec2(Quantize.FromPosition(v.PositionX), Quantize.FromPosition(v.PositionY));
                if (Vec2.Distance(p, eye) > aoi + AoiTolerance)
                    Assert.Fail("tick " + view.Tick + ": slot " + ownSlot + " got fighter " + view.Fighters.SlotAt(i) + " beyond the AOI");
            }
            Vec2 serverEye = server.Fighters[ownSlot].Position;
            int count = server.Globals.FighterCount;
            for (int j = 0; j < count; j++)
            {
                if (j == ownSlot) continue;
                ref FighterState f = ref server.Fighters[j];
                if (!f.Active || !f.Alive || !f.IsVisibleTo(team)) continue;
                if (Vec2.Distance(f.Position, serverEye) <= aoi + FarFighterMargin) continue;
                farFighters++;
                if (view.Fighters.Contains(j)) Assert.Fail("tick " + view.Tick + ": slot " + ownSlot + " got the far fighter " + j);
            }
            for (int i = 0; i < view.Projectiles.Count; i++)
            {
                int s = view.Projectiles.SlotAt(i);
                ref NetProjectileSegment seg = ref view.Projectiles.ItemAt(i);
                ref ProjectileState p = ref server.Projectiles[s];
                if (seg.Ended || !p.Active || p.SpawnTick != seg.SpawnTick) continue;
                if (Vec2.Distance(p.Position, serverEye) > aoi + FarProjectileMargin)
                    Assert.Fail("tick " + view.Tick + ": slot " + ownSlot + " got the far projectile " + s);
            }
            for (int s = 0; s < server.Projectiles.Length; s++)
                if (server.Projectiles[s].Active && Vec2.Distance(server.Projectiles[s].Position, serverEye) > aoi + FarProjectileMargin)
                    farProjectiles++;
        }
    }
}
