using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Client;
using Arena.Net.Protocol;
using Arena.Net.Server.Match;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Lobby
{
    /// <summary>
    /// What the .NET host process (S3.9) needs from the node: the stop request (Server-Spec §5.3) refuses new players like
    /// maintenance, sends <c>ServerNotice(Shutdown, seconds)</c>, cannot be undone, and the running match plays to the end;
    /// <see cref="IMatchResultSink"/> gets each ended match once; the room deadlines the game thread sleeps until are exact.
    /// </summary>
    public sealed class ShutdownTests
    {
        private const int ShortMatchSeconds = 10;
        private const int DrainSeconds = 180;

        private sealed class RecordingSink : IMatchResultSink
        {
            public int Count;
            public int EndTick = -1;
            public int WinnerTeam = -2;

            public void OnMatchEnded(MatchRoom room)
            {
                Count++;
                EndTick = room.Result.EndTick;
                WinnerTeam = room.Result.WinnerTeam;
            }
        }

        [Test]
        public void Shutdown_RefusesNewPlayers_RunningMatchEnds_ResultSinkOnce()
        {
            var rig = new LobbyRig(seed: 21);
            rig.Bundle.Acorns.DurationTicks = ShortMatchSeconds * SimLimits.TickRate; // server and clients share the defs
            var sink = new RecordingSink();
            rig.Node.Matches.ResultSink = sink;
            LobbyTestClient[] players = rig.AddSearching(6, ModeKind.Acorns);
            Assert.IsTrue(rig.RunUntil(() => rig.Rooms().Count == 1 && rig.Rooms()[0].Phase == MatchPhase.Running, 8f));
            MatchRoom room = rig.Rooms()[0];
            Assert.AreEqual(1, rig.Node.Matches.PlayingMatches);

            rig.Node.BeginShutdown(DrainSeconds);
            Assert.IsTrue(rig.Node.ShuttingDown);
            Assert.IsTrue(rig.Node.Maintenance, "a stopping node refuses like maintenance");
            Assert.IsTrue(rig.RunUntil(() => LobbyRig.All(players, p => p.Lobby.HasNotice), 1f), "ServerNotice to everyone");
            Assert.AreEqual(ServerNoticeKind.Shutdown, players[0].Lobby.Notice.Kind);
            Assert.AreEqual(DrainSeconds, players[0].Lobby.Notice.Seconds);

            LobbyTestClient late = rig.AddSearching(1, ModeKind.Acorns)[0];
            Assert.IsTrue(rig.RunUntil(() => late.Lobby.State == LobbyState.Failed, 2f), "new lobby connections refused");
            Assert.AreEqual(RejectReason.Maintenance, late.Lobby.ConnectRejectReason);
            rig.Node.SetMaintenance(false);
            Assert.IsTrue(rig.Node.Maintenance, "maintenance off does not undo a shutdown");

            Assert.AreEqual(0, sink.Count);
            Assert.IsTrue(rig.RunUntil(() => LobbyRig.All(players, p => p.Session.HasEnded), ShortMatchSeconds + 5f), "the running match plays to the end");
            Assert.AreEqual(0, rig.Node.Matches.PlayingMatches);
            Assert.AreEqual(MatchPhase.Ended, room.Phase, "the room lingers for returning players");
            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(players[0].Session.Ended.EndTick, sink.EndTick);
            Assert.AreEqual(players[0].Session.Ended.WinnerTeam, sink.WinnerTeam);
            rig.RunSeconds(1f);
            Assert.AreEqual(1, sink.Count, "the sink gets a match once");
        }

        [Test]
        public void NextStepDue_IsTheExactDeadlineOfTheNextStep()
        {
            var rig = new LobbyRig(seed: 22);
            Assert.AreEqual(long.MaxValue, rig.Node.Matches.NextStepDueMicros(), "no rooms — no deadline");
            rig.AddSearching(6, ModeKind.Acorns);
            Assert.IsTrue(rig.RunUntil(() => rig.Rooms().Count == 1, 2f));
            MatchRoom room = rig.Rooms()[0];
            for (int i = 0; i < 10; i++)
            {
                rig.Step();
                long due = room.NextStepMicros;
                Assert.AreEqual(due, rig.Node.Matches.NextStepDueMicros());
                Assert.Greater(due, rig.Now, "caught up: the next step is in the future");
                Assert.AreEqual(0, room.TicksDue(due - 1));
                Assert.AreEqual(1, room.TicksDue(due));
            }
        }
    }
}
