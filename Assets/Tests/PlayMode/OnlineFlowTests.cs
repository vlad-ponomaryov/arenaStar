using System;
using System.Collections;
using System.Net;
using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Net.Content;
using Arena.Net.Server.Lobby;
using Arena.Net.Server.Match;
using Arena.Net.Server.Transport;
using Arena.Net.Transport;
using Arena.Online;
using Arena.UI.Hud;
using Arena.UI.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// The whole online flow (S3.11, Server-Spec §12.3): menu → "Play 3 vs 3" → matchmaking screen → match (a server in the
    /// process on 127.0.0.1, bots fill after 1 s, a short Acorns match) → results → "Play again" → the menu with the search
    /// running → match → results → "Menu" → the menu, connection closed. The HUD shows the ping badge and the connection banner
    /// stays hidden on a healthy connection.
    /// </summary>
    public sealed class OnlineFlowTests
    {
        private const float StepTimeoutSeconds = 30f;
        private const float BotFillSeconds = 1f;
        private const int ShortMatchSeconds = 6;

        private InProcessServerPump _pump;
        private PortableUdpServerSocket _serverSocket;

        [TearDown]
        public void TearDown()
        {
            if (_pump != null) Object.Destroy(_pump.gameObject);
            _pump = null;
            if (_serverSocket != null) _serverSocket.Close();
            _serverSocket = null;
            if (OnlineSession.Instance != null) Object.Destroy(OnlineSession.Instance.gameObject);
            SceneFlow.ClearLaunchParams();
        }

        [UnityTest]
        public IEnumerator Menu_Queue_Match_Results_PlayAgain_Results_Menu()
        {
            SceneFlow.ClearLaunchParams();
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Single);
            MenuScreen menu = null;
            yield return WaitFor(() => OnlineSession.Instance != null && (menu = FindMenu()) != null, "Boot loads the menu");
            OnlineSession online = OnlineSession.Instance;
            Assert.IsTrue(online.IsAvailable);
            Assert.IsTrue(menu.PlayOnlineButton.interactable, "online buttons enabled");
            Assert.IsNotNull(menu.LastBeastButton);
            Assert.IsNotNull(menu.TrainingButton);
            Assert.IsFalse(menu.Matchmaking.IsShown, "no search yet");
            Assert.IsFalse(string.IsNullOrEmpty(menu.RegionButton.GetComponentInChildren<UnityEngine.UI.Text>().text), "region name shown");

            ContentBundle bundle = ContentPackReader.Read(online.Config.ContentPack.bytes, true, out ContentPackHeader header);
            bundle.Acorns.DurationTicks = ShortMatchSeconds * SimLimits.TickRate;
            online.Bundle.Acorns.DurationTicks = bundle.Acorns.DurationTicks; // client and server share the defs
            ServerSettings settings = ServerSettings.CreateDefault();
            settings.BotFillSeconds = BotFillSeconds;
            _serverSocket = new PortableUdpServerSocket(new IPEndPoint(IPAddress.Loopback, 0));
            var node = new ServerNode(_serverSocket, TransportSettings.CreateDefault(), settings, bundle, header.ContentHash,
                new CryptoNetRandom());
            _pump = new GameObject("InProcessServer").AddComponent<InProcessServerPump>();
            _pump.Node = node;
            online.ServerOverride = new IPEndPoint(IPAddress.Loopback, _serverSocket.LocalEndPoint.Port);

            // Round 1: the menu button.
            menu.PlayOnlineButton.onClick.Invoke();
            Assert.IsTrue(online.IsBusy, "searching");
            Assert.AreEqual(ModeKind.Acorns, online.SearchMode);
            yield return null;
            Assert.IsTrue(menu.Matchmaking.IsShown, "the matchmaking screen covers the menu");
            Assert.IsTrue(menu.Matchmaking.CancelButton.gameObject.activeInHierarchy, "Cancel visible");
            Assert.IsFalse(string.IsNullOrEmpty(menu.Matchmaking.ClockText.text), "the waiting clock runs");

            MatchHud hud = null;
            yield return PlayToResults(h => hud = h, "round 1");
            Assert.IsNotNull(hud.Ping, "ping badge online");
            Assert.IsNotNull(hud.Connection, "connection banner online");
            Assert.AreEqual(ConnectionOverlayState.Ok, hud.Connection.State, "healthy connection: no banner");
            Assert.Greater(hud.Ping.ShownMs, 0, "the ping badge shows the RTT");
            Assert.AreEqual(0, hud.Results.ShownPlace, "Acorns: a team result, no place");

            // "Play again": back in the queue with the same mode and fighter; the menu shows the search.
            hud.Results.AgainButton.onClick.Invoke();
            Assert.IsTrue(online.IsBusy, "Play again queues at once");
            yield return WaitFor(() => (menu = FindMenu()) != null && menu.Matchmaking != null && menu.Matchmaking.IsShown
                || SceneManager.GetActiveScene().name == SceneNames.Match, "the menu with the matchmaking screen");

            yield return PlayToResults(h => hud = h, "round 2");

            hud.Results.MenuButton.onClick.Invoke();
            yield return WaitFor(() => (menu = FindMenu()) != null, "back in the menu");
            Assert.AreEqual(OnlinePhase.Idle, online.Phase, "Menu closes the connection");
            Assert.IsNull(online.Net);
            Assert.IsFalse(menu.Matchmaking.IsShown);
            Assert.IsTrue(string.IsNullOrEmpty(menu.OnlineStatus.text), "no error");
        }

        private static IEnumerator PlayToResults(Action<MatchHud> found, string round)
        {
            MatchRunner runner = null;
            yield return WaitFor(() => (runner = FindOnlineRunner()) != null, round + ": online match scene started");
            MatchHud hud = Object.FindFirstObjectByType<MatchHud>();
            Assert.IsNotNull(hud);
            found(hud);
            yield return WaitFor(() => hud.Results != null && hud.Results.IsShown, round + ": the results after MatchEnded");
            Assert.IsTrue(runner.IsMatchEnded);
            Assert.IsTrue(runner.enabled, round + ": the runner did not stop on an exception");
            Assert.IsTrue(hud.Results.AgainButton.gameObject.activeInHierarchy);
            Assert.IsTrue(hud.Results.MenuButton.gameObject.activeInHierarchy);
        }

        private static MenuScreen FindMenu()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Menu) return null;
            MenuScreen m = Object.FindFirstObjectByType<MenuScreen>();
            return m != null && m.PlayOnlineButton != null ? m : null;
        }

        private static MatchRunner FindOnlineRunner()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Match) return null;
            MatchRunner r = Object.FindFirstObjectByType<MatchRunner>();
            return r != null && r.Driver is OnlineMatchDriver ? r : null;
        }

        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            float end = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("timed out: " + what);
                yield return null;
            }
        }
    }
}
