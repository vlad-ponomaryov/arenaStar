using Arena.Net.Client;
using Arena.UI.Hud;
using NUnit.Framework;

namespace Arena.Tests.EditMode.UI
{
    /// <summary>F1 (Stage3-TestAudit, Unity review 1): the banner never leaves the player in an online match with no way out.</summary>
    public sealed class ConnectionOverlayTests
    {
        private const long Lost = 1000000L;

        [Test]
        public void ServerClosedTheConnection_NothingRetrying_IsGaveUp_WithMenu()
        {
            ConnectionOverlayState s = ConnectionOverlay.Classify(false, ReconnectState.None, false, 0L, Lost, false);
            Assert.AreEqual(ConnectionOverlayState.GaveUp, s, "no endless countdown to 0");
            Assert.IsTrue(ConnectionOverlay.ShowsMenu(s));
        }

        [Test]
        public void WhileReconnecting_TheMenuIsAvailable()
        {
            Assert.AreEqual(ConnectionOverlayState.Lost,
                ConnectionOverlay.Classify(false, ReconnectState.Waiting, false, 5 * Lost, Lost, false));
            Assert.AreEqual(ConnectionOverlayState.Lost,
                ConnectionOverlay.Classify(false, ReconnectState.Connecting, false, 5 * Lost, Lost, false));
            Assert.AreEqual(ConnectionOverlayState.Lost,
                ConnectionOverlay.Classify(false, ReconnectState.None, true, 2 * Lost, Lost, false), "connected but silent");
            Assert.IsTrue(ConnectionOverlay.ShowsMenu(ConnectionOverlayState.Lost));
            Assert.IsTrue(ConnectionOverlay.ShowsMenu(ConnectionOverlayState.GaveUp));
            Assert.IsTrue(ConnectionOverlay.ShowsMenu(ConnectionOverlayState.MatchOver));
        }

        [Test]
        public void HealthyOrEnded_NoBanner_BotNotice_NoMenu()
        {
            Assert.AreEqual(ConnectionOverlayState.Ok, ConnectionOverlay.Classify(false, ReconnectState.None, true, 0L, Lost, false));
            Assert.AreEqual(ConnectionOverlayState.Ok, ConnectionOverlay.Classify(true, ReconnectState.None, false, 0L, Lost, false));
            Assert.AreEqual(ConnectionOverlayState.BotPlaying,
                ConnectionOverlay.Classify(false, ReconnectState.None, true, 0L, Lost, true));
            Assert.IsFalse(ConnectionOverlay.ShowsMenu(ConnectionOverlayState.BotPlaying));
            Assert.IsFalse(ConnectionOverlay.ShowsMenu(ConnectionOverlayState.Ok));
            Assert.AreEqual(ConnectionOverlayState.MatchOver,
                ConnectionOverlay.Classify(false, ReconnectState.MatchOver, false, 0L, Lost, false));
            Assert.AreEqual(ConnectionOverlayState.GaveUp,
                ConnectionOverlay.Classify(false, ReconnectState.GaveUp, false, 0L, Lost, false));
        }
    }
}
