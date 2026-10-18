using Arena.Bots;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Server.Diagnostics;
using Arena.Net.Server.Match;
using Arena.Net.Snapshots;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Server
{
    /// <summary>
    /// The match server with scripted clients over the virtual network (Server-Spec §11.1 "Сервер", S3.6): a full Acorns match
    /// in CI conditions (RTT 150 ms, 50 ms jitter, 2 % loss) from <c>MatchStart</c> to <c>MatchEnded</c> for everyone; inputs land
    /// on their ticks; the countdown runs on negative ticks and the simulation starts exactly at T = 0; the input log replays to
    /// the same state; takeover by a bot for silence, AFK and <c>Leave</c> and the return; join timeout; Last Beast
    /// <c>Eliminated</c>; <see cref="MatchHost.Tick"/> does not allocate.
    /// </summary>
    public sealed class MatchHostTests
    {
        private const float AcornsMatchSeconds = 175f;
        private const float LastBeastMatchSeconds = 400f;
        private const double MaxMissingShareCi = 0.01;

        [Test]
        public void FullAcornsMatch_TwoScriptedPlayers_CiConditions_EndsForEveryone_AndReplays()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 0, 4 }, seed: 11, conditions: MatchServerRig.CiConditions());
            for (int k = 0; k < rig.Clients.Length; k++)
            {
                int salt = k * 7 + 3;
                rig.Clients[k].Script = (tick, latest) => MatchServerRig.Wander(tick, salt);
            }
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(rig.AllEnded, AcornsMatchSeconds), "both clients get MatchEnded");

            MatchRoom room = rig.Room;
            Assert.AreEqual(MatchPhase.Ended, room.Phase);
            Assert.IsTrue(room.Simulation.IsEnded);
            for (int k = 0; k < rig.Clients.Length; k++)
            {
                ScriptedMatchClient c = rig.Clients[k];
                Assert.IsTrue(c.HasStart, "MatchStart");
                Assert.AreEqual(rig.ClientSlots[k], c.Start.OwnSlot);
                Assert.AreEqual(room.SlotCount, c.Start.SlotCount);
                Assert.AreEqual(rig.Host.Settings.CountdownTicks, c.Start.CountdownTicks);
                Assert.Less(c.FirstServerTick, 0, "snapshots already in the countdown");
                Assert.AreEqual(room.Result.EndTick, c.Ended.EndTick);
                Assert.AreEqual(room.Result.WinnerTeam, c.Ended.WinnerTeam);
                Assert.AreEqual(room.Result.MvpSlot, c.Ended.MvpSlot);
                for (int s = 0; s < room.SlotCount; s++)
                    Assert.AreEqual(room.Result.Slots[s].DamageDealt, c.Ended.Slots[s].DamageDealt, "stats of slot " + s);
                Assert.AreEqual(0, c.Malformed, "no malformed messages");
                Assert.Greater(c.SnapshotsDecoded, 4000);
            }
            Assert.AreEqual(room.Simulation.State.Mode.EndTick, room.Result.EndTick);

            ServerMetrics m = rig.Host.Metrics;
            TestContext.WriteLine("missing input share " + m.MissingInputShare.ToString("P2") + ", late " + m.LateInputs
                + ", carried presses " + m.PressesCarried + ", avg snapshot " + m.AverageSnapshotBytes.ToString("F0") + " B, max "
                + m.MaxSnapshotBytes + " B, keyframes " + m.Keyframes + ", takeovers " + m.Takeovers);
            Assert.Less(m.MissingInputShare, MaxMissingShareCi, "late and missing inputs < 1 % of ticks in CI conditions");
            Assert.AreEqual(0, m.MalformedMessages);
            Assert.AreEqual(0, m.SnapshotsNotFitting);
            Assert.AreEqual(0, room.Takeover.TakeoverCount, "nobody was taken over");

            Assert.IsTrue(ReplayVerifier.Verify(room.Defs, room.Setup, room.InputLog, room.Simulation.State, out string error), error);
        }

        [Test]
        public void Inputs_AreAppliedOnTheirTicks()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 1 }, seed: 3, conditions: MatchServerRig.CiConditions());
            rig.Clients[0].Script = (tick, latest) => MatchServerRig.Wander(tick, 5);
            rig.ConnectAll();
            rig.RunUntil(() => rig.Room.Tick >= 600, 40f);
            InputLog log = rig.Room.InputLog;
            int matching = 0, checkedTicks = 0;
            for (int t = 60; t < log.Ticks; t++)
            {
                checkedTicks++;
                PlayerInput expected = Quantize.Input(MatchServerRig.Wander(t, 5));
                PlayerInput got = log.Get(t, 1);
                if (got.Move == expected.Move && got.Buttons == expected.Buttons) matching++;
            }
            Assert.Greater(checkedTicks, 400);
            Assert.GreaterOrEqual(matching, checkedTicks * 99 / 100, "the input of tick t is simulated at tick t");
        }

        [Test]
        public void Countdown_RunsOnNegativeTicks_SimulationStartsAtZero_InputsBeforeZeroIgnored()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 0 }, seed: 5);
            ScriptedMatchClient c = rig.Clients[0];
            c.Script = (tick, latest) => new PlayerInput(new Vec2(1f, 0f), Vec2.Zero, tick < 0 ? InputButtons.Attack : InputButtons.None);
            Vec2 spawn = rig.Room.Setup.Slots[0].SpawnPosition;
            int countdownSnapshots = 0;
            bool zeroChecked = false;
            c.OnSnapshot = frame =>
            {
                if (frame.Tick < 0)
                {
                    countdownSnapshots++;
                    Assert.AreEqual(spawn, frame.Own.Position, "the fighter stands in the countdown");
                }
                if (frame.Tick == 0)
                {
                    zeroChecked = true;
                    Assert.AreEqual(spawn, frame.Own.Position, "tick 0 is the start state");
                }
            };
            rig.ConnectAll();
            int maxCountdownTick = int.MinValue;
            Assert.IsTrue(rig.RunUntil(() =>
            {
                MatchRoom r = rig.Room;
                if (r.Phase == MatchPhase.Countdown)
                {
                    Assert.Less(r.Tick, 0);
                    Assert.AreEqual(0, r.Simulation.State.Globals.Tick, "no simulation tick in the countdown");
                    if (r.Tick > maxCountdownTick) maxCountdownTick = r.Tick;
                }
                return r.Phase == MatchPhase.Running && r.Tick >= 30;
            }, 20f));
            Assert.AreEqual(-1, maxCountdownTick, "countdown runs up to −1");
            Assert.IsTrue(zeroChecked);
            Assert.Greater(countdownSnapshots, rig.Host.Settings.CountdownTicks / 2);
            Assert.AreEqual(rig.Room.Simulation.State.Globals.Tick, rig.Room.InputLog.Ticks, "log starts at tick 0");
            Assert.AreEqual(rig.Room.Tick, rig.Room.Simulation.State.Globals.Tick);
            for (int t = 0; t < rig.Room.InputLog.Ticks; t++)
                Assert.IsFalse(rig.Room.InputLog.Get(t, 0).Has(InputButtons.Attack), "countdown presses are not carried into the fight");
            Assert.Greater(Vec2.Distance(rig.Room.Simulation.State.Fighters[0].Position, spawn), 0.5f, "the fight inputs move the fighter");
        }

        [Test]
        public void Silence_BotTakesOver_AndInputReturnsControl()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 2 }, seed: 9);
            ScriptedMatchClient c = rig.Clients[0];
            c.Script = (tick, latest) => MatchServerRig.Wander(tick, 1);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 60, 20f));
            Assert.IsFalse(rig.Room.Takeover.IsBot(2));

            c.Silent = true;
            rig.RunSeconds(rig.Host.Settings.BotTakeoverSeconds - 0.3f);
            Assert.IsFalse(rig.Room.Takeover.IsBot(2), "not before BotTakeoverSeconds");
            rig.RunSeconds(0.6f);
            Assert.IsTrue(rig.Room.Takeover.IsBot(2), "a bot after BotTakeoverSeconds without input");
            Assert.IsTrue(rig.Room.Takeover.Bots.IsBot(2));
            Assert.AreEqual(BotControlReason.NoInput, rig.Room.Takeover.ReasonsOf(2));
            long flagged = c.BotControlledSnapshots;
            rig.RunSeconds(0.5f);
            Assert.Greater(c.BotControlledSnapshots, flagged, "the snapshot says a bot drives the fighter");

            c.Silent = false;
            rig.RunSeconds(0.3f);
            Assert.IsFalse(rig.Room.Takeover.IsBot(2), "input returns the control");
            Assert.IsFalse(rig.Room.Takeover.Bots.IsBot(2));
            Assert.IsFalse(c.Latest.Header.BotControlled);
            Assert.AreEqual(1, rig.Room.Takeover.TakeoverCount);
        }

        [Test]
        public void Afk_BotTakesOver_UntilAMove()
        {
            ServerSettings settings = MatchServerRig.CreateSettings();
            settings.AfkSeconds = 2f;
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 0 }, seed: 4, settings: settings);
            bool idle = true;
            rig.Clients[0].Script = (tick, latest) => idle ? default : new PlayerInput(new Vec2(0f, 1f), Vec2.Zero, InputButtons.None);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running, 20f));
            rig.RunSeconds(1.5f);
            Assert.IsFalse(rig.Room.Takeover.IsBot(0));
            rig.RunSeconds(1f);
            Assert.AreEqual(BotControlReason.Afk, rig.Room.Takeover.ReasonsOf(0), "only zero inputs for AfkSeconds");
            idle = false;
            rig.RunSeconds(0.5f);
            Assert.IsFalse(rig.Room.Takeover.IsBot(0), "a move returns the control");
            Assert.IsTrue(rig.Room.Takeover.WasAfk(0), "the AFK flag stays for the result");
        }

        [Test]
        public void Leave_BotAtOnce_AndStays()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 3 }, seed: 6);
            ScriptedMatchClient c = rig.Clients[0];
            c.Script = (tick, latest) => MatchServerRig.Wander(tick, 2);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 30, 20f));
            c.Leave();
            rig.RunSeconds(0.3f);
            Assert.IsTrue((rig.Room.Takeover.ReasonsOf(3) & BotControlReason.Left) != 0);
            Assert.IsTrue(rig.Room.Takeover.Bots.IsBot(3));
            rig.RunSeconds(1f);
            Assert.IsTrue(rig.Room.Takeover.IsBot(3), "inputs after Leave do not return the control");
            Assert.AreEqual(1, rig.Host.Metrics.Leaves);
        }

        [Test]
        public void JoinTimeout_MissingPlayerIsABot_MatchStarts()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 0, 1 }, seed: 8);
            rig.Clients[0].Script = (tick, latest) => MatchServerRig.Wander(tick, 4);
            rig.Connect(0); // client 1 never comes
            float limit = rig.Host.Settings.JoinTimeoutSeconds + rig.Host.Settings.CountdownSeconds + 2f;
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 30, limit));
            Assert.IsTrue(rig.Room.Takeover.IsBot(1), "who did not come is a bot");
            Assert.IsFalse(rig.Room.Takeover.IsBot(0));
            Assert.IsTrue(rig.Clients[0].HasStart);
        }

        [Test]
        public void LoadTimeout_StartsWithoutClientReady()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 0 }, seed: 12);
            rig.Clients[0].WithholdReady = true;
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Loading, 5f));
            rig.RunSeconds(rig.Host.Settings.LoadTimeoutSeconds - 1f);
            Assert.AreEqual(MatchPhase.Loading, rig.Room.Phase, "waits for ClientReady");
            rig.RunSeconds(1.5f);
            Assert.AreEqual(MatchPhase.Countdown, rig.Room.Phase, "LoadTimeoutSeconds passed");
        }

        [Test]
        public void WrongToken_IsRejected()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 0 }, seed: 13);
            rig.Clients[0].Connect(rig.Sockets[0], new NetToken(1ul, 2ul), rig.Now);
            rig.RunSeconds(1f);
            Assert.AreEqual(ClientState.Disconnected, rig.Clients[0].Transport.State);
            Assert.AreEqual(RejectReason.InvalidToken, rig.Clients[0].Transport.RejectReason);
            Assert.AreEqual(1, rig.Host.Metrics.RejectedConnections);
        }

        [Test]
        public void LastBeast_EliminatedPlayer_GetsPlaceAndStats_ThenMatchEnded()
        {
            var rig = new MatchServerRig(ModeKind.LastBeast, new[] { 0, 5 }, seed: 21);
            for (int k = 0; k < rig.Clients.Length; k++)
            {
                int salt = k * 5 + 1;
                rig.Clients[k].Script = (tick, latest) => MatchServerRig.Wander(tick, salt);
            }
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(rig.AllEnded, LastBeastMatchSeconds), "MatchEnded to everyone");
            for (int k = 0; k < rig.Clients.Length; k++)
            {
                ScriptedMatchClient c = rig.Clients[k];
                int slot = rig.ClientSlots[k];
                int place = rig.Room.Result.Slots[slot].Place;
                Assert.Greater(place, 0, "a place for every slot");
                Assert.AreEqual(place, c.Ended.Slots[slot].Place);
                // Everyone out gets Eliminated — also the runner-up who falls in the end tick (S3.15); one still standing at the
                // time limit gets MatchEnded only.
                bool standing = rig.Room.Simulation.State.Fighters[slot].Alive;
                if (place > 1 && !standing) Assert.IsTrue(c.HasEliminated, "slot " + slot + " (place " + place + ") got Eliminated");
                if (c.HasEliminated)
                {
                    Assert.AreEqual(slot, c.Eliminated.Slot);
                    Assert.AreEqual(place, c.Eliminated.Result.Place, "Eliminated carries the final place");
                }
                if (place == 1) Assert.IsFalse(c.HasEliminated, "the winner is not eliminated");
                TestContext.WriteLine("slot " + slot + ": place " + place + (c.HasEliminated ? ", Eliminated" : ""));
            }
            Assert.IsTrue(ReplayVerifier.Verify(rig.Room.Defs, rig.Room.Setup, rig.Room.InputLog, rig.Room.Simulation.State, out string error), error);
        }

        [Test]
        public void Room_ClosesAfterLinger()
        {
            ServerSettings settings = MatchServerRig.CreateSettings();
            settings.ResultLingerSeconds = 1f;
            var rig = new MatchServerRig(ModeKind.LastBeast, new[] { 0 }, seed: 2, settings: settings);
            rig.Clients[0].Script = (tick, latest) => MatchServerRig.Wander(tick, 3);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(rig.AllEnded, LastBeastMatchSeconds));
            int index = rig.Room.Index;
            rig.RunSeconds(1.5f);
            Assert.IsNull(rig.Host.GetRoom(index), "closed rooms are removed");
            Assert.AreEqual(0, rig.Host.Metrics.ActiveMatches);
            Assert.AreEqual(0, rig.Host.Metrics.ConnectedPlayers);
        }

        [Test]
        public void HostTick_DoesNotAllocate_InSteadyState()
        {
            var rig = new MatchServerRig(ModeKind.Acorns, new[] { 0, 3 }, seed: 17, conditions: MatchServerRig.CiConditions());
            for (int k = 0; k < rig.Clients.Length; k++)
            {
                int salt = k + 2;
                rig.Clients[k].Script = (tick, latest) => MatchServerRig.Wander(tick, salt);
            }
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 300, 30f));
            AllocationAssert.InitializeTypes(typeof(MatchSimulation).Assembly, typeof(BotController).Assembly, typeof(MatchHost).Assembly,
                typeof(SnapshotFrame).Assembly);
            rig.RunSeconds(1f);
            MatchHost host = rig.Host;
            InMemoryNetwork network = rig.Network;
            ScriptedMatchClient[] clients = rig.Clients;
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    network.Advance(MatchServerRig.FrameMicros);
                    host.Tick(network.NowMicros);
                    for (int k = 0; k < clients.Length; k++) clients[k].Update(network.NowMicros);
                }
            }, "MatchHost.Tick (and the scripted clients)");
        }
    }
}
