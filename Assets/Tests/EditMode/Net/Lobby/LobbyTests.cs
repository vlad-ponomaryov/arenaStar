using System.Collections.Generic;
using Arena.Bots;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Server.Lobby;
using Arena.Net.Server.Match;
using Arena.Net.Snapshots;
using Arena.Net.Transport;
using Arena.Tests.EditMode.Core;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Lobby
{
    /// <summary>
    /// Lobby and matchmaking over the virtual network (Server-Spec §6, §11.1 "Лобби", S3.8): 12 clients make two full 3×3
    /// matches; 1–5 clients get a match with bots at the 15th second, players split evenly; equal fighters go to different
    /// teams; cancel and a dropped connection leave no ticket; Acorns and Last Beast assemble in parallel; maintenance refuses
    /// joins and connections while the running match plays on; invalid mode or fighter is refused; the lobby connection
    /// carries <c>MatchStart</c> without a reconnect; the queued steady state does not allocate.
    /// </summary>
    public sealed class LobbyTests
    {
        private const float AssignSeconds = 2f;
        private const float StartSeconds = 6f;

        [Test]
        public void TwelveClients_FormTwoFullAcornsMatches_WithoutBots()
        {
            var rig = new LobbyRig(seed: 3);
            LobbyTestClient[] clients = rig.AddSearching(12, ModeKind.Acorns);
            Assert.IsTrue(rig.RunUntil(() => LobbyRig.All(clients, c => c.Lobby.State == LobbyState.Assigned), AssignSeconds),
                "all 12 assigned at once");
            Assert.AreEqual(2, rig.Metrics.MatchesAssembled);
            Assert.AreEqual(0, rig.Metrics.BotFilledMatches);
            Assert.AreEqual(0, rig.Metrics.QueuedPlayers);
            Assert.AreEqual(0, rig.Matchmaker.QueueOf(ModeKind.Acorns).Count);
            List<MatchRoom> rooms = rig.Rooms();
            Assert.AreEqual(2, rooms.Count);
            foreach (MatchRoom room in rooms)
            {
                Assert.AreEqual(ModeKind.Acorns, room.Mode);
                Assert.AreEqual(6, room.SlotCount);
                for (int s = 0; s < room.SlotCount; s++) Assert.IsTrue(room.GetSlot(s).IsPlayer, "no bots in a full match");
            }

            var seen = new HashSet<string>();
            foreach (LobbyTestClient c in clients)
            {
                MatchAssignedMessage a = c.Lobby.Assignment;
                Assert.IsTrue(a.Address.IsEmpty, "the match runs on this server");
                Assert.IsTrue(seen.Add(a.MatchId + ":" + a.Slot), "every client has its own slot");
                MatchRoom room = rooms.Find(r => r.MatchId == a.MatchId);
                Assert.IsNotNull(room);
                Assert.IsTrue(room.GetSlot(a.Slot).Token.Equals(a.RejoinToken), "the rejoin token is the slot ticket");
                Assert.AreEqual(c.Lobby.FighterIndex, room.Setup.Slots[a.Slot].FighterIndex, "the chosen fighter");
            }

            Assert.IsTrue(rig.RunUntil(() => LobbyRig.All(clients, c => c.Session.HasStart), StartSeconds), "MatchStart on the lobby connection");
            long handshakes = 0;
            foreach (LobbyTestClient c in clients)
            {
                Assert.AreEqual(c.Lobby.Assignment.Slot, c.Session.OwnSlot);
                Assert.AreEqual(c.Lobby.Assignment.MatchId, c.Session.Start.MatchId);
                handshakes += c.Net.Transport.HandshakePacketsSent;
            }
            Assert.IsTrue(rig.RunUntil(() => rooms.TrueForAll(r => r.Phase == MatchPhase.Running), StartSeconds), "both matches run");
            foreach (LobbyTestClient c in clients) Assert.IsTrue(c.Net.IsConnected);
            long after = 0;
            foreach (LobbyTestClient c in clients) after += c.Net.Transport.HandshakePacketsSent;
            Assert.AreEqual(handshakes, after, "no reconnect into the match");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void FewClients_GetBotsAtTheFifteenthSecond_PlayersSplitEvenly(int players)
        {
            var rig = new LobbyRig(seed: 10 + players);
            LobbyTestClient[] clients = rig.AddSearching(players, ModeKind.Acorns);
            long joined = rig.Now;
            rig.RunSeconds(14.5f);
            Assert.AreEqual(0, rig.Metrics.MatchesAssembled, "nobody is assembled before BotFillSeconds");
            foreach (LobbyTestClient c in clients)
            {
                Assert.AreEqual(LobbyState.Searching, c.Lobby.State);
                Assert.IsTrue(c.Lobby.HasStatus, "QueueStatus arrives");
                Assert.AreEqual(players, c.Lobby.Status.Found);
                Assert.AreEqual(6, c.Lobby.Status.Needed);
                Assert.GreaterOrEqual(c.Lobby.Status.WaitSeconds, 13);
            }
            Assert.IsTrue(rig.RunUntil(() => rig.Metrics.MatchesAssembled == 1, 1f));
            long waited = rig.Now - joined;
            Assert.GreaterOrEqual(waited, 15000000L, "bot fill at the 15th second");
            Assert.Less(waited, 15100000L);
            Assert.AreEqual(1, rig.Metrics.BotFilledMatches);
            Assert.AreEqual(6 - players, rig.Metrics.FillBots);

            MatchRoom room = rig.Rooms()[0];
            int team0 = 0, team1 = 0;
            for (int s = 0; s < room.SlotCount; s++)
            {
                if (!room.GetSlot(s).IsPlayer) continue;
                if (room.Setup.Slots[s].Team == 0) team0++;
                else team1++;
            }
            Assert.AreEqual((players + 1) / 2, team0, "team 0 players");
            Assert.AreEqual(players / 2, team1, "team 1 players");
            Assert.IsTrue(rig.RunUntil(() => LobbyRig.All(clients, c => c.Session.HasStart), StartSeconds));
            Assert.IsTrue(rig.RunUntil(() => room.Phase == MatchPhase.Running, StartSeconds), "the match starts with the bots");
        }

        [Test]
        public void EqualFighters_GoToDifferentTeams()
        {
            var rig = new LobbyRig(seed: 21);
            rig.AddSearching(4, ModeKind.Acorns, new[] { 2, 2, 3, 3 });
            Assert.IsTrue(rig.RunUntil(() => rig.Metrics.MatchesAssembled == 1, 16f));
            MatchRoom room = rig.Rooms()[0];
            for (int team = 0; team < 2; team++)
            {
                var fighters = new HashSet<int>();
                for (int s = 0; s < room.SlotCount; s++)
                    if (room.Setup.Slots[s].Team == team)
                        Assert.IsTrue(fighters.Add(room.Setup.Slots[s].FighterIndex), "no equal fighters in team " + team);
            }
        }

        [Test]
        public void BestSplit_MinimizesEqualFightersInATeam()
        {
            int mask = RoomAssembler.BestSplit(new[] { 0, 0, 1, 1 }, 4);
            Assert.AreEqual(2, PopCount(mask));
            Assert.AreNotEqual((mask >> 0) & 1, (mask >> 1) & 1, "the two 0s apart");
            Assert.AreNotEqual((mask >> 2) & 1, (mask >> 3) & 1, "the two 1s apart");
            Assert.AreEqual(0b01, RoomAssembler.BestSplit(new[] { 4, 4 }, 2), "2 players: one per team");
            Assert.AreEqual(1, RoomAssembler.BestSplit(new[] { 5 }, 1));
            mask = RoomAssembler.BestSplit(new[] { 0, 0, 0, 1, 1, 1 }, 6);
            Assert.AreEqual(3, PopCount(mask), "3 + 3 even when equal fighters are unavoidable");
        }

        [Test]
        public void Assembler_PutsPlayersFirstInTheirTeam_AndFreeForAllInOrder()
        {
            var rig = new LobbyRig();
            RoomAssembler assembler = rig.Matchmaker.Assembler;
            Assert.AreEqual(6, assembler.Capacity(ModeKind.Acorns, out int teamSize));
            Assert.AreEqual(3, teamSize);
            Assert.AreEqual(SimLimits.MaxFighters, assembler.Capacity(ModeKind.LastBeast, out int ffaTeam));
            Assert.AreEqual(0, ffaTeam);
            Assert.IsFalse(assembler.IsPlayable(ModeKind.None));

            var slots = new int[SimLimits.MaxFighters];
            MatchRoomRequest r = assembler.Assemble(ModeKind.Acorns, new[] { 0, 1, 2 }, 3, 99, slots);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, new[] { slots[0], slots[1], slots[2] }, "2 + 1, first slots of each team");
            Assert.AreEqual(6, r.Setup.SlotCount);
            Assert.AreEqual(3, CountTrue(r.PlayerSlots));

            r = assembler.Assemble(ModeKind.LastBeast, new[] { 0, 0, 1 }, 3, 99, slots);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, new[] { slots[0], slots[1], slots[2] });
            Assert.AreEqual(SimLimits.MaxFighters, r.Setup.SlotCount);
            Assert.AreEqual(0, r.Setup.Slots[0].FighterIndex);
            Assert.AreEqual(0, r.Setup.Slots[1].FighterIndex, "equal fighters are allowed in free-for-all");
        }

        [Test]
        public void Cancel_RemovesTheTicket_AndTheConnectionCanSearchAgain()
        {
            var rig = new LobbyRig(seed: 5);
            LobbyTestClient c = rig.AddSearching(1, ModeKind.Acorns)[0];
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.HasStatus, 2f));
            Assert.AreEqual(1, rig.Metrics.QueuedPlayers);
            c.Lobby.Cancel();
            Assert.AreEqual(LobbyState.Idle, c.Lobby.State);
            Assert.IsTrue(rig.RunUntil(() => rig.Metrics.QueuedPlayers == 0, 1f));
            Assert.AreEqual(1, rig.Metrics.Cancels);
            Assert.IsFalse(rig.Matchmaker.TicketOf(0).Active);
            rig.RunSeconds(16f);
            Assert.AreEqual(0, rig.Metrics.MatchesAssembled, "no match for a cancelled ticket");
            Assert.IsTrue(c.Net.IsConnected, "the connection stays (keepalive)");

            long handshakes = c.Net.Transport.HandshakePacketsSent;
            c.Lobby.Search(c.Socket, ModeKind.LastBeast, 1, rig.Now);
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.HasStatus, 2f));
            Assert.AreEqual(ModeKind.LastBeast, c.Lobby.Status.Mode);
            Assert.AreEqual(SimLimits.MaxFighters, c.Lobby.Status.Needed);
            Assert.AreEqual(handshakes, c.Net.Transport.HandshakePacketsSent, "the second search needs no handshake");
        }

        [Test]
        public void DroppedConnection_InTheQueue_LosesItsTicket_AfterTheTimeout()
        {
            var rig = new LobbyRig(seed: 6);
            LobbyTestClient c = rig.AddSearching(1, ModeKind.Acorns)[0];
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.HasStatus, 2f));
            c.Paused = true;
            long paused = rig.Now;
            Assert.IsTrue(rig.RunUntil(() => rig.Metrics.QueuedPlayers == 0, 7f), "ticket removed");
            long gone = rig.Now - paused;
            Assert.GreaterOrEqual(gone, (long)rig.Transport.DisconnectTimeoutMs * 1000L - 1100000L, "not before DisconnectTimeout");
            Assert.AreEqual(1, rig.Metrics.DroppedTickets);
            Assert.AreEqual(0, rig.Metrics.LobbyConnections);
            rig.RunSeconds(15f);
            Assert.AreEqual(0, rig.Metrics.MatchesAssembled, "no match for a dropped ticket");
            Assert.AreEqual(0, rig.Matchmaker.QueueOf(ModeKind.Acorns).Count);
        }

        [Test]
        public void AcornsAndLastBeast_AssembleInParallel()
        {
            var rig = new LobbyRig(seed: 8);
            LobbyTestClient[] acorns = rig.AddSearching(2, ModeKind.Acorns);
            LobbyTestClient[] beasts = rig.AddSearching(3, ModeKind.LastBeast);
            Assert.IsTrue(rig.RunUntil(() => rig.Metrics.MatchesAssembled == 2, 16f));
            List<MatchRoom> rooms = rig.Rooms();
            MatchRoom a = rooms.Find(r => r.Mode == ModeKind.Acorns);
            MatchRoom b = rooms.Find(r => r.Mode == ModeKind.LastBeast);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.AreEqual(6, a.SlotCount);
            Assert.AreEqual(SimLimits.MaxFighters, b.SlotCount);
            for (int k = 0; k < beasts.Length; k++)
            {
                Assert.AreEqual(b.MatchId, beasts[k].Lobby.Assignment.MatchId);
                Assert.AreEqual(k, beasts[k].Lobby.Assignment.Slot, "free-for-all: join order");
            }
            foreach (LobbyTestClient c in acorns) Assert.AreEqual(a.MatchId, c.Lobby.Assignment.MatchId);
            Assert.IsTrue(rig.RunUntil(() => a.Phase == MatchPhase.Running && b.Phase == MatchPhase.Running, StartSeconds));
        }

        [Test]
        public void Maintenance_RefusesJoinsAndConnections_RunningMatchPlaysOn()
        {
            var rig = new LobbyRig(seed: 9);
            LobbyTestClient[] players = rig.AddSearching(6, ModeKind.Acorns);
            Assert.IsTrue(rig.RunUntil(() => rig.Metrics.MatchesAssembled == 1, AssignSeconds));
            MatchRoom room = rig.Rooms()[0];
            Assert.IsTrue(rig.RunUntil(() => room.Phase == MatchPhase.Running, StartSeconds));
            LobbyTestClient queued = rig.AddSearching(1, ModeKind.Acorns)[0];
            Assert.IsTrue(rig.RunUntil(() => queued.Lobby.HasStatus, 2f));

            rig.Node.SetMaintenance(true);
            Assert.IsTrue(rig.RunUntil(() => queued.Lobby.State == LobbyState.Rejected, 1f));
            Assert.AreEqual(QueueRejectReason.Maintenance, queued.Lobby.QueueRejectReason);
            Assert.IsTrue(rig.RunUntil(() => queued.Lobby.HasNotice && LobbyRig.All(players, p => p.Lobby.HasNotice), 1f), "ServerNotice to everyone");
            Assert.AreEqual(ServerNoticeKind.Maintenance, players[0].Lobby.Notice.Kind);
            Assert.AreEqual(0, rig.Metrics.QueuedPlayers);

            queued.Lobby.Search(queued.Socket, ModeKind.Acorns, 0, rig.Now);
            Assert.IsTrue(rig.RunUntil(() => queued.Lobby.State == LobbyState.Rejected, 1f), "QueueJoin refused in maintenance");
            Assert.AreEqual(QueueRejectReason.Maintenance, queued.Lobby.QueueRejectReason);

            LobbyTestClient late = rig.AddSearching(1, ModeKind.Acorns)[0];
            Assert.IsTrue(rig.RunUntil(() => late.Lobby.State == LobbyState.Failed, 2f), "new lobby connections refused");
            Assert.AreEqual(RejectReason.Maintenance, late.Lobby.ConnectRejectReason);

            int tick = room.Tick;
            rig.RunSeconds(3f);
            Assert.AreEqual(MatchPhase.Running, room.Phase, "the running match plays on");
            Assert.Greater(room.Tick, tick + 80);
            foreach (LobbyTestClient p in players) Assert.IsTrue(p.Net.IsConnected);

            rig.Node.SetMaintenance(false);
            queued.Lobby.Search(queued.Socket, ModeKind.Acorns, 0, rig.Now);
            Assert.IsTrue(rig.RunUntil(() => queued.Lobby.State == LobbyState.Searching && queued.Lobby.HasStatus, 2f));
        }

        [Test]
        public void InvalidModeOrFighter_IsRefused()
        {
            var rig = new LobbyRig(seed: 12);
            LobbyTestClient c = rig.AddSearching(1, ModeKind.Acorns, new[] { rig.Bundle.Fighters.Length })[0];
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.State == LobbyState.Rejected, 2f));
            Assert.AreEqual(QueueRejectReason.InvalidFighter, c.Lobby.QueueRejectReason);

            c.Lobby.Search(c.Socket, ModeKind.None, 0, rig.Now);
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.State == LobbyState.Rejected, 1f));
            Assert.AreEqual(QueueRejectReason.InvalidMode, c.Lobby.QueueRejectReason);

            c.Lobby.Search(c.Socket, (ModeKind)77, 0, rig.Now);
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.State == LobbyState.Rejected, 1f));
            Assert.AreEqual(QueueRejectReason.InvalidMode, c.Lobby.QueueRejectReason);
            Assert.AreEqual(3, rig.Metrics.RejectedJoins);
            Assert.AreEqual(0, rig.Metrics.QueuedPlayers);

            c.Lobby.Search(c.Socket, ModeKind.Acorns, 0, rig.Now);
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.HasStatus, 2f), "a valid join after refusals");
            Assert.AreEqual(1, rig.Metrics.QueuedPlayers);
        }

        [Test]
        public void SecondJoin_ReplacesTheTicket()
        {
            var rig = new LobbyRig(seed: 13);
            LobbyTestClient c = rig.AddSearching(1, ModeKind.Acorns)[0];
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.HasStatus, 2f));
            c.Lobby.Search(c.Socket, ModeKind.LastBeast, 2, rig.Now);
            Assert.IsTrue(rig.RunUntil(() => c.Lobby.HasStatus, 2f));
            Assert.AreEqual(1, rig.Metrics.QueuedPlayers, "one ticket per connection");
            Assert.AreEqual(0, rig.Matchmaker.QueueOf(ModeKind.Acorns).Count);
            Assert.AreEqual(1, rig.Matchmaker.QueueOf(ModeKind.LastBeast).Count);
        }

        [Test]
        public void QueuedSteadyState_DoesNotAllocate()
        {
            var rig = new LobbyRig(seed: 14);
            rig.AddSearching(4, ModeKind.LastBeast);
            Assert.IsTrue(rig.RunUntil(() => LobbyRig.All(rig.Clients.ToArray(), c => c.Lobby.HasStatus), 2f));
            AllocationAssert.InitializeTypes(typeof(ServerNode).Assembly, typeof(NetClient).Assembly, typeof(BotController).Assembly,
                typeof(SnapshotFrame).Assembly);
            rig.RunSeconds(1f);
            ServerNode node = rig.Node;
            InMemoryNetwork network = rig.Network;
            LobbyTestClient[] clients = rig.Clients.ToArray();
            AllocationAssert.NoGcAllocations(() =>
            {
                for (int i = 0; i < 150; i++)
                {
                    network.Advance(LobbyRig.FrameMicros);
                    long now = network.NowMicros;
                    node.Update(now);
                    for (int k = 0; k < clients.Length; k++)
                    {
                        clients[k].Net.Update(now);
                        clients[k].Lobby.Update(now);
                    }
                }
            }, "ServerNode.Update and lobby clients while queued");
            Assert.Greater(rig.Metrics.StatusesSent, 8);
        }

        private static int PopCount(int v)
        {
            int n = 0;
            for (; v != 0; v &= v - 1) n++;
            return n;
        }

        private static int CountTrue(bool[] values)
        {
            int n = 0;
            foreach (bool v in values)
                if (v) n++;
            return n;
        }
    }
}
