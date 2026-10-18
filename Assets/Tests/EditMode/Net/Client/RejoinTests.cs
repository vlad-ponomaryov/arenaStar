using Arena.Core.Input;
using Arena.Core.Modes;
using Arena.Gameplay.Settings;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Server.Match;
using Arena.Net.Transport;
using Arena.Online;
using Arena.Tests.EditMode.Net.Server;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// Losing and getting back the match connection (Server-Spec §7, S3.12) with real match clients over the virtual network:
    /// a 10 s outage (bot → automatic <c>Rejoin</c> → the player drives again), the "61 s" case (the attempts give up, "Back to the
    /// match" still works while the match runs), a restarted client with the saved ticket, a changed address in the fight
    /// (the same connection moves), AFK 30 s (bot until an input), <c>Leave</c> (bot at once, no reconnect), a return after the end
    /// (the result) and after the room closed (<c>MatchOver</c>), a pause of the app (the clocks restart instead of catching up).
    /// </summary>
    public sealed class RejoinTests
    {
        private const int Own = 0;

        private static MatchClientRig StartAcorns(int seed, out NetAddress blocked, out bool[] block)
        {
            var rig = new MatchClientRig(ModeKind.Acorns, new[] { 0, 3 }, seed: seed);
            rig.Scripts[0] = tick => MatchServerRig.Wander(tick, 1);
            rig.Scripts[1] = tick => MatchServerRig.Wander(tick, 5);
            NetAddress address = rig.Sockets[Own].Local;
            var flag = new bool[1];
            rig.Network.SendFilter = (from, to, buffer, offset, length) => !flag[0] || (from != address && to != address);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 90
                && rig.Sessions[Own].Predictor != null && rig.Sessions[Own].Predictor.Ready, 30f), "the match runs");
            blocked = address;
            block = flag;
            return rig;
        }

        private static void AssertPlayerDrives(MatchClientRig rig, int slot, MatchClientSession session, string context)
        {
            int applied = rig.Room.GetSlot(slot).Input.LastAppliedInputTick;
            rig.RunSeconds(1f);
            Assert.IsTrue(rig.Room.GetSlot(slot).IsConnected, context + ": the slot has a connection");
            Assert.IsFalse(rig.Room.Takeover.IsBot(slot), context + ": the bot left the slot");
            Assert.Greater(rig.Room.GetSlot(slot).Input.LastAppliedInputTick, applied + 20, context + ": the player's inputs are applied");
            Assert.IsNotNull(session.Latest, context + ": snapshots again");
            Assert.IsFalse(session.Latest.Header.BotControlled, context + ": the snapshot says the player drives");
            Assert.IsTrue(session.Predictor.Ready && !session.Predictor.BotControlled, context + ": the own fighter is predicted");
            Assert.IsTrue(session.Presentation.HasState, context + ": the display is built");
        }

        [Test]
        public void Outage10s_BotTakesOver_ThenTheClientReturnsByItself()
        {
            MatchClientRig rig = StartAcorns(21, out _, out bool[] block);
            NetClient client = rig.Clients[Own];
            MatchClientSession session = rig.Sessions[Own];
            ulong oldId = client.Transport.Connection.Id;

            block[0] = true;
            rig.RunSeconds(2f);
            Assert.IsTrue(rig.Room.Takeover.IsBot(Own), "a bot plays the silent slot");
            rig.RunSeconds(4f);
            Assert.IsFalse(client.IsConnected, "the client timed out");
            Assert.IsTrue(client.ReconnectState == ReconnectState.Waiting || client.ReconnectState == ReconnectState.Connecting);
            float left = client.ReconnectSecondsLeft(rig.Now);
            Assert.That(left, Is.InRange(50f, 56f), "the window counts from the last packet");
            rig.RunSeconds(4f);
            Assert.IsTrue(rig.Room.Takeover.IsBot(Own));

            block[0] = false;
            Assert.IsTrue(rig.RunUntil(() => client.IsConnected && session.Latest != null && session.Predictor.Ready, 10f),
                "back within one retry after the outage");
            TestContext.WriteLine("reconnect attempts " + client.ReconnectAttempts + ", rejoins " + session.Rejoins);
            Assert.AreEqual(1, client.Reconnects);
            Assert.AreEqual(1, session.Rejoins);
            Assert.AreNotEqual(oldId, client.Transport.Connection.Id, "a new connection");
            Assert.AreEqual(ReconnectState.None, client.ReconnectState);
            Assert.AreEqual(1, rig.Host.Metrics.Rejoins);
            AssertPlayerDrives(rig, Own, session, "after the outage");
            Assert.IsTrue(rig.Sessions[1].Predictor.Ready && !rig.Room.Takeover.IsBot(3), "the other player was not affected");
        }

        [Test]
        public void Outage61s_AttemptsGiveUp_ButBackToTheMatchWorksWhileItRuns()
        {
            MatchClientRig rig = StartAcorns(23, out _, out bool[] block);
            NetClient client = rig.Clients[Own];
            MatchClientSession session = rig.Sessions[Own];

            block[0] = true;
            rig.RunSeconds(59f);
            Assert.AreNotEqual(ReconnectState.GaveUp, client.ReconnectState, "still trying inside the window");
            rig.RunSeconds(2f);
            Assert.AreEqual(ReconnectState.GaveUp, client.ReconnectState, "no more attempts after 60 s");
            int attempts = client.ReconnectAttempts;
            // Timeout 5 s, then pauses 0.5, 1, 2, 3, 5, 5 … s between handshakes of up to 5 s each: 7 attempts in 60 s.
            TestContext.WriteLine("attempts in the window: " + attempts);
            Assert.That(attempts, Is.InRange(6, 8), "attempts by the schedule");

            block[0] = false;
            rig.RunSeconds(5f);
            Assert.IsFalse(client.IsConnected, "nothing automatic after the window");
            Assert.AreEqual(attempts, client.ReconnectAttempts);
            Assert.IsTrue(rig.Room.Takeover.IsBot(Own), "the bot keeps playing");

            Assert.IsTrue(client.RetryRejoin(rig.Now), "\"Back to the match\"");
            Assert.IsTrue(rig.RunUntil(() => client.IsConnected && session.Latest != null && session.Predictor.Ready, 5f));
            AssertPlayerDrives(rig, Own, session, "after 61 s");
        }

        [Test]
        public void RestartedClient_ReturnsWithTheSavedTicket()
        {
            MatchClientRig rig = StartAcorns(25, out _, out _);
            var store = new RejoinStore(new MemoryKeyValueStore(), 60f);
            NetClient old = rig.Clients[Own];
            var saved = new RejoinTicket
            {
                Server = MatchServerRig.ServerAddress, MatchId = rig.Room.MatchId, Slot = Own, Token = old.RejoinToken,
                SavedAtSeconds = 1000
            };
            store.Save(in saved);

            // The app is killed: the old client is never updated again; the new one starts from the store on a new port.
            rig.Paused[Own] = true;
            rig.RunSeconds(3f);
            Assert.IsTrue(rig.Room.Takeover.IsBot(Own));
            Assert.IsTrue(store.TryLoad(1020, out RejoinTicket ticket));
            var client = new NetClient(TransportSettings.CreateDefault(), 0x5EEDu, new DeterministicNetRandom(99));
            var session = new MatchClientSession(client, rig.Bundle, ClientMatchSettings.CreateDefault());
            InMemoryClientSocket socket = rig.Network.CreateClientSocket(NetAddress.FromIPv4(10, 0, 2, 1, 6000), ticket.Server);
            client.Connect(socket, ConnectPurpose.Rejoin, ticket.Token, rig.Now);
            rig.Clients[Own] = client;
            rig.Sessions[Own] = session;
            rig.Sockets[Own] = socket;
            rig.Paused[Own] = false;

            Assert.IsTrue(rig.RunUntil(() => session.HasStart && session.Latest != null && session.Predictor.Ready, 5f));
            Assert.AreEqual(Own, session.OwnSlot);
            Assert.AreEqual(rig.Room.MatchId, session.Start.MatchId);
            Assert.AreEqual(0, session.Rejoins, "a fresh session: nothing to reset");
            AssertPlayerDrives(rig, Own, session, "after a restart");
        }

        [Test]
        public void AddressChange_InTheFight_MovesTheSameConnection()
        {
            MatchClientRig rig = StartAcorns(27, out _, out _);
            NetClient client = rig.Clients[Own];
            MatchClientSession session = rig.Sessions[Own];
            ulong id = client.Transport.Connection.Id;
            InMemoryClientSocket replacement = rig.Network.CreateClientSocket(NetAddress.FromIPv4(10, 0, 3, 1, 6100),
                MatchServerRig.ServerAddress);
            client.SocketFactory = () => replacement;

            rig.Sockets[Own].BreakPath(); // Android moved from Wi-Fi to mobile data
            rig.RunSeconds(1f);
            Assert.AreEqual(1, client.SocketsReplaced);
            Assert.IsTrue(client.IsConnected);
            Assert.AreEqual(id, client.Transport.Connection.Id, "the same connection");
            Assert.AreEqual(0, client.Reconnects, "no rejoin needed");
            Assert.AreEqual(replacement.Local, rig.Host.Transport.GetConnection(rig.Room.GetSlot(Own).Connection).Address,
                "the server moved the connection to the new address");
            AssertPlayerDrives(rig, Own, session, "after the address change");
            Assert.AreEqual(0, rig.Room.Takeover.TakeoverCount, "no bot was needed");
        }

        [Test]
        public void Afk30s_BotTakesOver_UntilTheNextInput()
        {
            MatchClientRig rig = StartAcorns(29, out _, out _);
            MatchClientSession session = rig.Sessions[Own];
            rig.Scripts[Own] = tick => default; // connected, but the sticks are not touched

            rig.RunSeconds(rig.Host.Settings.AfkSeconds - 1f);
            Assert.IsFalse(rig.Room.Takeover.IsBot(Own), "not before AfkSeconds");
            rig.RunSeconds(1.5f);
            Assert.IsTrue(rig.Room.Takeover.IsBot(Own), "a bot after AfkSeconds of zero input");
            Assert.AreEqual(BotControlReason.Afk, rig.Room.Takeover.ReasonsOf(Own));
            rig.RunSeconds(0.5f);
            Assert.IsTrue(session.Latest.Header.BotControlled, "the client is told");
            Assert.IsTrue(session.Predictor.BotControlled, "no prediction while the bot drives");

            rig.Scripts[Own] = tick => MatchServerRig.Wander(tick, 3);
            rig.RunSeconds(0.5f);
            Assert.IsFalse(rig.Room.Takeover.IsBot(Own), "a move returns the control");
            Assert.IsTrue(rig.Room.Takeover.WasAfk(Own), "the result carries the AFK flag");
            AssertPlayerDrives(rig, Own, session, "after AFK");
            Assert.IsTrue(rig.Clients[Own].IsConnected);
            Assert.AreEqual(0, rig.Clients[Own].Reconnects);
        }

        [Test]
        public void Leave_BotAtOnce_AndNoAutomaticReturn()
        {
            MatchClientRig rig = StartAcorns(31, out _, out _);
            NetClient client = rig.Clients[Own];
            rig.Sessions[Own].Leave();
            rig.RunSeconds(0.3f);
            Assert.IsTrue(rig.Room.Takeover.IsBot(Own), "a bot at once");
            Assert.AreEqual(BotControlReason.Left, rig.Room.Takeover.ReasonsOf(Own) & BotControlReason.Left);
            client.Disconnect(); // what the game does after Leave
            rig.RunSeconds(10f);
            Assert.AreEqual(ReconnectState.None, client.ReconnectState);
            Assert.AreEqual(0, client.ReconnectAttempts, "closed on purpose: no return");
            Assert.IsTrue(rig.Room.Takeover.IsBot(Own));
            Assert.AreEqual(1, rig.Host.Metrics.Leaves);
        }

        [Test]
        public void ReturnAfterTheEnd_GetsTheResult_AndAfterTheRoomClosed_MatchOver()
        {
            var rig = new MatchClientRig(ModeKind.LastBeast, new[] { 0, 1 }, seed: 33);
            rig.Scripts[0] = tick => MatchServerRig.Wander(tick, 1);
            rig.Scripts[1] = tick => MatchServerRig.Wander(tick, 2);
            NetAddress address = rig.Sockets[Own].Local;
            bool blocked = false;
            rig.Network.SendFilter = (from, to, buffer, offset, length) => !blocked || (from != address && to != address);
            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 30, 30f));
            NetClient client = rig.Clients[Own];
            MatchClientSession session = rig.Sessions[Own];
            NetToken token = client.RejoinToken;

            blocked = true;
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase >= MatchPhase.Ended, 400f), "the bots finish the match");
            Assert.IsFalse(session.HasEnded, "the result did not reach the cut-off client");
            blocked = false;
            if (client.ReconnectState == ReconnectState.GaveUp) Assert.IsTrue(client.RetryRejoin(rig.Now));
            Assert.IsTrue(rig.RunUntil(() => session.HasEnded, 10f), "the return during the linger gets MatchEnded");
            Assert.AreEqual(rig.Room.Result.EndTick, session.Ended.EndTick);
            Assert.AreEqual(rig.Room.Result.MvpSlot, session.Ended.MvpSlot);
            Assert.AreEqual(MatchPhase.Ended, rig.Room.Phase);
            rig.RunSeconds(0.2f);
            Assert.IsFalse(client.ReconnectEnabled, "nothing to return to after the result");

            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Closed || rig.Host.GetRoom(rig.Room.Index) == null,
                rig.Host.Settings.ResultLingerSeconds + 2f));
            var late = new NetClient(TransportSettings.CreateDefault(), 0x5EEDu, new DeterministicNetRandom(77));
            InMemoryClientSocket socket = rig.Network.CreateClientSocket(NetAddress.FromIPv4(10, 0, 4, 1, 6200),
                MatchServerRig.ServerAddress);
            late.Connect(socket, ConnectPurpose.Rejoin, token, rig.Now);
            for (int i = 0; i < 60 && late.Transport.State != ClientState.Disconnected; i++)
            {
                rig.Step();
                late.Update(rig.Now);
            }
            Assert.AreEqual(ClientState.Disconnected, late.Transport.State);
            Assert.AreEqual(RejectReason.MatchOver, late.Transport.RejectReason, "the room is gone: MatchOver");
            Assert.AreEqual(ReconnectState.None, late.ReconnectState, "a first connect is not a return");
        }

        [Test]
        public void LostReturn_ToAClosedRoom_EndsWithMatchOver()
        {
            MatchClientRig rig = StartAcorns(35, out _, out bool[] block);
            NetClient client = rig.Clients[Own];
            block[0] = true;
            rig.RunSeconds(6f);
            Assert.IsFalse(client.IsConnected);
            Assert.IsTrue(rig.RunUntil(() => client.ReconnectState == ReconnectState.Connecting, 10f));
            block[0] = false;
            ForceClose(rig.Room);
            Assert.IsTrue(rig.RunUntil(() => client.ReconnectState == ReconnectState.MatchOver, 15f));
            Assert.IsFalse(client.IsConnected);
            int attempts = client.ReconnectAttempts;
            rig.RunSeconds(10f);
            Assert.AreEqual(attempts, client.ReconnectAttempts, "no more attempts after MatchOver");
            Assert.IsFalse(client.RetryRejoin(rig.Now));
        }

        [Test]
        public void Pause3s_ClocksRestartInsteadOfCatchingUp_Pause8s_ReturnsByItself()
        {
            MatchClientRig rig = StartAcorns(37, out _, out _);
            NetClient client = rig.Clients[Own];
            MatchClientSession session = rig.Sessions[Own];

            rig.Paused[Own] = true;
            rig.RunSeconds(3f);
            rig.Paused[Own] = false;
            session.OnResumed(rig.Now);
            Assert.AreEqual(1, session.Resyncs);
            Assert.IsFalse(session.Clock.Started, "the clock waits for the next snapshot");
            rig.RunSeconds(1.5f);
            Assert.IsTrue(client.IsConnected);
            Assert.AreEqual(0, client.Reconnects, "3 s is inside the timeout");
            int lead = session.PredictionTick - session.NewestServerTick;
            Assert.That(lead, Is.InRange(0, 15), "the prediction tick is near the server again, not 90 ticks behind");
            AssertPlayerDrives(rig, Own, session, "after a 3 s pause");

            rig.Paused[Own] = true;
            rig.RunSeconds(8f);
            rig.Paused[Own] = false;
            session.OnResumed(rig.Now);
            Assert.IsTrue(rig.RunUntil(() => client.Reconnects == 1 && session.Latest != null && session.Predictor.Ready, 10f),
                "the dead connection returns by itself");
            AssertPlayerDrives(rig, Own, session, "after an 8 s pause");
        }

        /// <summary>
        /// The room ends and lingers out while the client is away — shortened: the room is put into <c>Closed</c> (the host
        /// removes it on its next pass), as a 3-minute Acorns match would after its linger.
        /// </summary>
        private static void ForceClose(MatchRoom room) =>
            typeof(MatchRoom).GetProperty(nameof(MatchRoom.Phase)).SetValue(room, MatchPhase.Closed);
    }
}
